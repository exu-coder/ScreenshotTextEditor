using System.Windows;
using System.Windows.Media;

namespace ScreenshotTextEditor.Models;

public class TextRegion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OriginalText { get; set; } = string.Empty;
    public string ReplacementText { get; set; } = string.Empty;
    public Rect BoundingBox { get; set; }
    public float Confidence { get; set; }
    public double RotationDegrees { get; set; }
    public Color DetectedColor { get; set; } = Colors.Black;
    public double Opacity { get; set; } = 1.0;
    public string MatchedFontFamily { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 16;
    public FontWeight FontWeight { get; set; } = FontWeights.Normal;
    public FontStyle FontStyle { get; set; } = FontStyles.Normal;
    public double LetterSpacing { get; set; }
    public double HorizontalScale { get; set; } = 1.0;
    public bool IsSelected { get; set; }
    public bool IsEdited { get; set; }
    public bool IsVisible { get; set; } = true;
    public string LayerName { get; set; } = "Text";

    public bool HasReplacement => !string.IsNullOrWhiteSpace(ReplacementText);
}
