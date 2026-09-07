using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTextEditor.Models;
using Tesseract;
using Color = System.Windows.Media.Color;
using Rect = System.Windows.Rect;

namespace ScreenshotTextEditor.OCR;

/// <summary>
/// Fully local OCR engine using Tesseract.
/// No network calls.
/// </summary>
public class OcrEngine : IDisposable
{
    private TesseractEngine? _engine;
    private readonly string _tessDataPath;
    private bool _disposed;

    public OcrEngine(string? tessDataPath = null)
    {
        // Look for tessdata next to the executable or in Resources
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _tessDataPath = tessDataPath
            ?? Path.Combine(baseDir, "Resources", "tessdata")
            ?? Path.Combine(baseDir, "tessdata");

        if (!Directory.Exists(_tessDataPath))
        {
            // Fallback: try to create a minimal path; user must place eng.traineddata
            Directory.CreateDirectory(_tessDataPath);
        }

        try
        {
            _engine = new TesseractEngine(_tessDataPath, "eng", EngineMode.Default);
            _engine.SetVariable("tessedit_char_whitelist",
                "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?@#$%&*()_+-=[]{}|;:'\"<>/\\");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR init warning: {ex.Message}");
            // Engine remains null – callers must handle gracefully
        }
    }

    public bool IsAvailable => _engine != null;

    public async Task<List<TextRegion>> AnalyzeAsync(BitmapSource image, IProgress<int>? progress = null)
    {
        return await Task.Run(() => Analyze(image, progress));
    }

    public List<TextRegion> Analyze(BitmapSource image, IProgress<int>? progress = null)
    {
        var results = new List<TextRegion>();
        if (_engine == null || image == null) return results;

        try
        {
            using var bitmap = BitmapSourceToBitmap(image);
            using var pix = PixConverter.ToPix(bitmap);
            using var page = _engine.Process(pix);

            progress?.Report(30);

            using var iter = page.GetIterator();
            iter.Begin();

            int count = 0;
            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bounds))
                {
                    var text = iter.GetText(PageIteratorLevel.Word)?.Trim();
                    if (string.IsNullOrWhiteSpace(text) || text.Length < 1) continue;

                    float conf = iter.GetConfidence(PageIteratorLevel.Word) / 100f;

                    var region = new TextRegion
                    {
                        OriginalText = text,
                        BoundingBox = new Rect(bounds.X1, bounds.Y1, bounds.Width, bounds.Height),
                        Confidence = conf,
                        LayerName = text.Length > 24 ? text[..24] + "…" : text
                    };

                    // Sample dominant color from the region
                    region.DetectedColor = SampleDominantColor(bitmap, bounds);

                    results.Add(region);
                    count++;
                }
            } while (iter.Next(PageIteratorLevel.Word));

            progress?.Report(90);

            // Merge nearby words into lines for better UX (simple heuristic)
            results = MergeNearbyWords(results);
            progress?.Report(100);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OCR error: {ex.Message}");
        }

        return results;
    }

    public List<TextRegion> AnalyzeRegion(BitmapSource image, Int32Rect area)
    {
        // Crop and run OCR only on the selected area
        var cropped = new CroppedBitmap(image, area);
        var regions = Analyze(cropped);
        // Offset bounding boxes back to full image coordinates
        foreach (var r in regions)
        {
            r.BoundingBox = new Rect(
                r.BoundingBox.X + area.X,
                r.BoundingBox.Y + area.Y,
                r.BoundingBox.Width,
                r.BoundingBox.Height);
        }
        return regions;
    }

    private static List<TextRegion> MergeNearbyWords(List<TextRegion> words)
    {
        if (words.Count < 2) return words;

        // Simple left-to-right, top-to-bottom grouping into lines
        var sorted = words.OrderBy(w => w.BoundingBox.Y).ThenBy(w => w.BoundingBox.X).ToList();
        var lines = new List<TextRegion>();
        var current = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            var next = sorted[i];
            double verticalGap = Math.Abs(next.BoundingBox.Y - current.BoundingBox.Y);
            double horizontalGap = next.BoundingBox.X - (current.BoundingBox.X + current.BoundingBox.Width);

            // Same line if vertically close and horizontally reasonable
            if (verticalGap < current.BoundingBox.Height * 0.6 && horizontalGap < current.BoundingBox.Height * 2.5)
            {
                current.OriginalText += " " + next.OriginalText;
                current.BoundingBox = Rect.Union(current.BoundingBox, next.BoundingBox);
                current.Confidence = (current.Confidence + next.Confidence) / 2;
            }
            else
            {
                current.LayerName = current.OriginalText.Length > 24
                    ? current.OriginalText[..24] + "…"
                    : current.OriginalText;
                lines.Add(current);
                current = next;
            }
        }
        current.LayerName = current.OriginalText.Length > 24
            ? current.OriginalText[..24] + "…"
            : current.OriginalText;
        lines.Add(current);
        return lines;
    }

    private static Color SampleDominantColor(Bitmap bmp, Tesseract.Rect bounds)
    {
        try
        {
            int x1 = Math.Max(0, bounds.X1);
            int y1 = Math.Max(0, bounds.Y1);
            int x2 = Math.Min(bmp.Width - 1, bounds.X2);
            int y2 = Math.Min(bmp.Height - 1, bounds.Y2);

            long r = 0, g = 0, b = 0;
            int count = 0;

            // Sample every few pixels for speed
            for (int y = y1; y <= y2; y += 2)
            {
                for (int x = x1; x <= x2; x += 2)
                {
                    var px = bmp.GetPixel(x, y);
                    // Prefer darker pixels (likely text on light bg) or high contrast
                    if (px.GetBrightness() < 0.55f)
                    {
                        r += px.R; g += px.G; b += px.B;
                        count++;
                    }
                }
            }

            if (count == 0) return Colors.Black;
            return Color.FromRgb((byte)(r / count), (byte)(g / count), (byte)(b / count));
        }
        catch
        {
            return Colors.Black;
        }
    }

    private static Bitmap BitmapSourceToBitmap(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        return new Bitmap(ms);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _engine?.Dispose();
        _disposed = true;
    }
}
