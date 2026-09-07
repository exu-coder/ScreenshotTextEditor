using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;

namespace ScreenshotTextEditor.Models;

public class ProjectDocument
{
    public string? FilePath { get; set; }
    public string? OriginalImagePath { get; set; }
    public byte[]? OriginalImageBytes { get; set; }
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }
    public ObservableCollection<TextRegion> TextRegions { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0";
}
