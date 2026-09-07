using ScreenshotTextEditor.Models;

namespace ScreenshotTextEditor.Editing;

public class HistoryManager
{
    private readonly Stack<ProjectSnapshot> _undo = new();
    private readonly Stack<ProjectSnapshot> _redo = new();
    private const int MaxHistory = 50;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Push(ProjectDocument doc, byte[]? currentImageBytes)
    {
        _undo.Push(CreateSnapshot(doc, currentImageBytes));
        if (_undo.Count > MaxHistory) Trim();
        _redo.Clear();
    }

    public (ProjectDocument? Doc, byte[]? ImageBytes) Undo(ProjectDocument current, byte[]? currentImage)
    {
        if (!CanUndo) return (null, null);
        _redo.Push(CreateSnapshot(current, currentImage));
        var snap = _undo.Pop();
        return (snap.Document, snap.ImageBytes);
    }

    public (ProjectDocument? Doc, byte[]? ImageBytes) Redo(ProjectDocument current, byte[]? currentImage)
    {
        if (!CanRedo) return (null, null);
        _undo.Push(CreateSnapshot(current, currentImage));
        var snap = _redo.Pop();
        return (snap.Document, snap.ImageBytes);
    }

    private static ProjectSnapshot CreateSnapshot(ProjectDocument doc, byte[]? imageBytes)
    {
        var clone = new ProjectDocument
        {
            FilePath = doc.FilePath,
            OriginalImagePath = doc.OriginalImagePath,
            OriginalImageBytes = doc.OriginalImageBytes,
            ImageWidth = doc.ImageWidth,
            ImageHeight = doc.ImageHeight,
            CreatedAt = doc.CreatedAt,
            ModifiedAt = DateTime.UtcNow,
            Version = doc.Version
        };

        foreach (var r in doc.TextRegions)
        {
            clone.TextRegions.Add(new TextRegion
            {
                Id = r.Id,
                OriginalText = r.OriginalText,
                ReplacementText = r.ReplacementText,
                BoundingBox = r.BoundingBox,
                Confidence = r.Confidence,
                RotationDegrees = r.RotationDegrees,
                DetectedColor = r.DetectedColor,
                Opacity = r.Opacity,
                MatchedFontFamily = r.MatchedFontFamily,
                FontSize = r.FontSize,
                FontWeight = r.FontWeight,
                FontStyle = r.FontStyle,
                LetterSpacing = r.LetterSpacing,
                HorizontalScale = r.HorizontalScale,
                IsSelected = r.IsSelected,
                IsEdited = r.IsEdited,
                IsVisible = r.IsVisible,
                LayerName = r.LayerName
            });
        }

        return new ProjectSnapshot { Document = clone, ImageBytes = imageBytes };
    }

    private void Trim()
    {
        var arr = _undo.Reverse().Take(MaxHistory).Reverse().ToArray();
        _undo.Clear();
        foreach (var s in arr) _undo.Push(s);
    }

    private class ProjectSnapshot
    {
        public ProjectDocument Document { get; set; } = null!;
        public byte[]? ImageBytes { get; set; }
    }
}
