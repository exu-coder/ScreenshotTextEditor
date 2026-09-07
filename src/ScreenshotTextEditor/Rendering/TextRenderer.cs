using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenshotTextEditor.Models;
using SkiaSharp;

namespace ScreenshotTextEditor.Rendering;

/// <summary>
/// Renders replacement text onto an image using SkiaSharp,
/// matching size, color, weight and approximate position.
/// </summary>
public class TextRenderer
{
    public BitmapSource Composite(BitmapSource background, IEnumerable<TextRegion> regions)
    {
        int width = background.PixelWidth;
        int height = background.PixelHeight;

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // Draw original (or inpainted) background
        using (var bgImage = BitmapSourceToSKImage(background))
        {
            canvas.DrawImage(bgImage, 0, 0);
        }

        foreach (var region in regions.Where(r => r.IsVisible && r.HasReplacement))
        {
            DrawTextRegion(canvas, region);
        }

        using var snapshot = surface.Snapshot();
        return SKImageToBitmapSource(snapshot);
    }

    private void DrawTextRegion(SKCanvas canvas, TextRegion region)
    {
        var box = region.BoundingBox;
        float fontSize = (float)region.FontSize;

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(
                region.DetectedColor.R,
                region.DetectedColor.G,
                region.DetectedColor.B,
                (byte)(region.Opacity * 255)),
            TextSize = fontSize,
            Typeface = SKTypeface.FromFamilyName(
                region.MatchedFontFamily,
                region.FontWeight == FontWeights.Bold || region.FontWeight == FontWeights.SemiBold
                    ? SKFontStyleWeight.Bold
                    : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                region.FontStyle == FontStyles.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright),
            TextAlign = SKTextAlign.Left
        };

        // Optional letter spacing approximation via scale
        if (Math.Abs(region.HorizontalScale - 1.0) > 0.01)
        {
            canvas.Save();
            canvas.Scale((float)region.HorizontalScale, 1f, (float)box.X, (float)box.Y);
        }

        // Baseline adjustment – text is drawn from baseline
        float baseline = (float)(box.Y + box.Height * 0.78);

        if (Math.Abs(region.RotationDegrees) > 0.5)
        {
            canvas.Save();
            canvas.RotateDegrees((float)region.RotationDegrees, (float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
            canvas.DrawText(region.ReplacementText, (float)box.X, baseline, paint);
            canvas.Restore();
        }
        else
        {
            canvas.DrawText(region.ReplacementText, (float)box.X, baseline, paint);
        }

        if (Math.Abs(region.HorizontalScale - 1.0) > 0.01)
            canvas.Restore();
    }

    /// <summary>
    /// Auto-fit text length by adjusting horizontal scale / size so it roughly fits the original box.
    /// </summary>
    public void AutoFitText(TextRegion region)
    {
        if (string.IsNullOrEmpty(region.ReplacementText) || region.BoundingBox.Width <= 0)
            return;

        using var paint = new SKPaint
        {
            TextSize = (float)region.FontSize,
            Typeface = SKTypeface.FromFamilyName(region.MatchedFontFamily)
        };

        float measured = paint.MeasureText(region.ReplacementText);
        if (measured <= 0) return;

        float targetWidth = (float)region.BoundingBox.Width;
        float scale = targetWidth / measured;

        // Prefer moderate scaling over extreme size changes
        if (scale < 0.7f || scale > 1.4f)
        {
            // Also adjust font size a bit
            region.FontSize *= Math.Clamp(scale, 0.75, 1.25);
            region.HorizontalScale = Math.Clamp(scale / (float)(region.FontSize / (region.BoundingBox.Height * 0.78)), 0.7, 1.35);
        }
        else
        {
            region.HorizontalScale = scale;
        }
    }

    private static SKImage BitmapSourceToSKImage(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var skData = SKData.Create(ms);
        return SKImage.FromEncodedData(skData);
    }

    private static BitmapSource SKImageToBitmapSource(SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var ms = new MemoryStream(data.ToArray());
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}
