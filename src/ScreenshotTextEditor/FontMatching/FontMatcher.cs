using System.Drawing;
using System.Drawing.Text;
using System.Windows;
using System.Windows.Media;
using ScreenshotTextEditor.Models;
using FontFamily = System.Windows.Media.FontFamily;
using FontStyle = System.Windows.FontStyle;
using FontWeight = System.Windows.FontWeight;

namespace ScreenshotTextEditor.FontMatching;

/// <summary>
/// Best-effort local font matching.
/// Analyzes region size / aspect and maps to installed Windows fonts.
/// Perfect pixel-level font ID from raster is impossible; this provides the strongest practical approximation.
/// </summary>
public class FontMatcher
{
    private readonly List<string> _availableFonts;

    public FontMatcher()
    {
        _availableFonts = new List<string>();
        using var collection = new InstalledFontCollection();
        foreach (var f in collection.Families)
            _availableFonts.Add(f.Name);

        // Prefer modern UI fonts first
        _availableFonts = _availableFonts
            .OrderBy(f => PriorityScore(f))
            .ToList();
    }

    public void MatchFont(TextRegion region)
    {
        if (region.BoundingBox.Height <= 0) return;

        // Estimate font size from bounding box height (rough heuristic)
        region.FontSize = Math.Max(8, region.BoundingBox.Height * 0.78);

        // Aspect ratio of the text block gives a clue about monospace vs proportional
        double aspect = region.BoundingBox.Width / Math.Max(1, region.BoundingBox.Height);
        int charCount = Math.Max(1, region.OriginalText.Replace(" ", "").Length);
        double avgCharWidth = region.BoundingBox.Width / charCount;

        // Weight estimation from height vs typical ratios
        region.FontWeight = region.BoundingBox.Height > 28
            ? FontWeights.SemiBold
            : FontWeights.Normal;

        // Choose family
        region.MatchedFontFamily = ChooseBestFamily(aspect, avgCharWidth, region.OriginalText);
    }

    private string ChooseBestFamily(double aspect, double avgCharWidth, string sample)
    {
        // Heuristics
        bool looksMono = avgCharWidth > 0 && (aspect / Math.Max(1, sample.Length)) > 0.55;
        bool looksSerif = sample.Any(c => "il1".Contains(c)); // weak signal

        string[] preferredUi = { "Segoe UI", "Segoe UI Variable", "Arial", "Calibri", "Microsoft YaHei UI" };
        string[] preferredMono = { "Consolas", "Cascadia Mono", "Courier New", "Lucida Console" };
        string[] preferredSerif = { "Times New Roman", "Georgia", "Cambria", "Palatino Linotype" };

        var candidates = looksMono ? preferredMono
            : looksSerif ? preferredSerif
            : preferredUi;

        foreach (var name in candidates)
        {
            if (_availableFonts.Any(f => f.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }

        // Fallback to first available
        return _availableFonts.FirstOrDefault() ?? "Segoe UI";
    }

    private static int PriorityScore(string name)
    {
        return name switch
        {
            "Segoe UI" => 0,
            "Segoe UI Variable" => 1,
            "Arial" => 2,
            "Calibri" => 3,
            "Consolas" => 4,
            "Cascadia Mono" => 5,
            _ => 100
        };
    }

    public IReadOnlyList<string> GetInstalledFonts() => _availableFonts.AsReadOnly();
}
