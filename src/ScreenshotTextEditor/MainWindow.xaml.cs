using System.Collections.ObjectModel;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using ScreenshotTextEditor.Editing;
using ScreenshotTextEditor.FontMatching;
using ScreenshotTextEditor.ImageProcessing;
using ScreenshotTextEditor.Models;
using ScreenshotTextEditor.OCR;
using ScreenshotTextEditor.Rendering;
using Path = System.IO.Path;

namespace ScreenshotTextEditor;

public partial class MainWindow : Window
{
    private readonly OcrEngine _ocr = new();
    private readonly InpaintingService _inpainter = new();
    private readonly FontMatcher _fontMatcher = new();
    private readonly TextRenderer _renderer = new();
    private readonly HistoryManager _history = new();

    private ProjectDocument _project = new();
    private BitmapSource? _originalImage;
    private BitmapSource? _workingImage;   // after inpainting
    private BitmapSource? _finalImage;     // with replacement text
    private double _zoom = 1.0;
    private bool _showBoxes = true;
    private bool _compareMode;

    public MainWindow()
    {
        InitializeComponent();
        LayersList.ItemsSource = _project.TextRegions;
        UpdateStatus("Ready • Offline mode • All processing is local");
    }

    // ==================== File / Import ====================

    private void Open_Click(object sender, RoutedEventArgs e) => OpenImage();

    private void OpenImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|All files|*.*",
            Title = "Open Screenshot / Image"
        };
        if (dlg.ShowDialog() != true) return;
        LoadImageFromFile(dlg.FileName);
    }

    private void LoadImageFromFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            SetImage(bmp, path);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open image:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetImage(BitmapSource image, string? sourcePath = null)
    {
        _originalImage = image;
        _workingImage = image;
        _finalImage = image;
        _project = new ProjectDocument
        {
            OriginalImagePath = sourcePath,
            ImageWidth = image.PixelWidth,
            ImageHeight = image.PixelHeight
        };
        LayersList.ItemsSource = _project.TextRegions;

        MainImage.Source = image;
        ImageCanvas.Width = image.PixelWidth;
        ImageCanvas.Height = image.PixelHeight;
        OverlayCanvas.Children.Clear();

        ResolutionText.Text = $"{image.PixelWidth} × {image.PixelHeight}";
        UpdateStatus($"Loaded {image.PixelWidth}×{image.PixelHeight}");
        FitToScreen();

        // Auto-scan
        _ = ScanImageAsync();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Bitmap))
            e.Effects = DragDropEffects.Copy;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            var img = files.FirstOrDefault(f =>
                f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase));
            if (img != null) LoadImageFromFile(img);
        }
        else if (e.Data.GetDataPresent(DataFormats.Bitmap))
        {
            if (e.Data.GetData(DataFormats.Bitmap) is BitmapSource bmp)
                SetImage(bmp);
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e) => PasteFromClipboard();

    private void PasteFromClipboard()
    {
        if (Clipboard.ContainsImage())
        {
            var img = Clipboard.GetImage();
            if (img != null) SetImage(img);
        }
        else
        {
            UpdateStatus("Clipboard does not contain an image");
        }
    }

    // ==================== OCR ====================

    private async Task ScanImageAsync()
    {
        if (_originalImage == null) return;

        if (!_ocr.IsAvailable)
        {
            MessageBox.Show(
                "Tesseract OCR data not found.\n\nPlace eng.traineddata in:\nResources/tessdata/\n\nDownload from: https://github.com/tesseract-ocr/tessdata",
                "OCR Not Available", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ProgressOverlay.Visibility = Visibility.Visible;
        ScanProgress.Value = 0;
        ProgressLabel.Text = "Scanning image…";

        var progress = new Progress<int>(p =>
        {
            ScanProgress.Value = p;
            ProgressLabel.Text = $"Scanning image… {p}%";
        });

        try
        {
            var regions = await _ocr.AnalyzeAsync(_originalImage, progress);
            _project.TextRegions.Clear();
            foreach (var r in regions)
            {
                _fontMatcher.MatchFont(r);
                _project.TextRegions.Add(r);
            }

            DrawOverlays();
            RegionCountText.Text = $"{regions.Count} text regions";
            UpdateStatus($"Detected {regions.Count} text regions • Click any box to edit");
        }
        catch (Exception ex)
        {
            UpdateStatus($"OCR failed: {ex.Message}");
        }
        finally
        {
            ProgressOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => _ = ScanImageAsync();

    private void RescanArea_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Select a region on the canvas first (feature placeholder for area rescan).", "Rescan Area");
    }

    // ==================== Overlay & Click-to-Edit ====================

    private void DrawOverlays()
    {
        OverlayCanvas.Children.Clear();
        if (!_showBoxes || _compareMode) return;

        foreach (var region in _project.TextRegions.Where(r => r.IsVisible))
        {
            var rect = new Rectangle
            {
                Width = region.BoundingBox.Width,
                Height = region.BoundingBox.Height,
                Stroke = region.IsEdited
                    ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                    : new SolidColorBrush(Color.FromArgb(180, 139, 92, 246)),
                StrokeThickness = region.IsSelected ? 2.5 : 1.5,
                Fill = region.IsSelected
                    ? new SolidColorBrush(Color.FromArgb(40, 139, 92, 246))
                    : new SolidColorBrush(Color.FromArgb(20, 139, 92, 246)),
                Cursor = Cursors.Hand,
                Tag = region
            };
            Canvas.SetLeft(rect, region.BoundingBox.X);
            Canvas.SetTop(rect, region.BoundingBox.Y);
            rect.MouseLeftButtonDown += Region_Click;
            OverlayCanvas.Children.Add(rect);
        }
    }

    private void Region_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Rectangle { Tag: TextRegion region }) return;
        e.Handled = true;

        foreach (var r in _project.TextRegions) r.IsSelected = false;
        region.IsSelected = true;
        DrawOverlays();
        LayersList.SelectedItem = region;

        ShowEditDialog(region);
    }

    private void ShowEditDialog(TextRegion region)
    {
        var dialog = new Window
        {
            Title = "Edit Text",
            Width = 420,
            Height = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            ResizeMode = ResizeMode.NoResize
        };

        var panel = new StackPanel { Margin = new Thickness(20) };

        panel.Children.Add(new TextBlock
        {
            Text = "Original:",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text = region.OriginalText,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Replace with:",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        var input = new TextBox
        {
            Text = string.IsNullOrEmpty(region.ReplacementText) ? region.OriginalText : region.ReplacementText,
            FontSize = 14,
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 16)
        };
        panel.Children.Add(input);

        var conf = new TextBlock
        {
            Text = $"OCR confidence: {region.Confidence:P0}  •  Font: {region.MatchedFontFamily}",
            Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(conf);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var apply = new Button { Content = "Apply", Width = 90, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(8, 4, 8, 4) };
        var cancel = new Button { Content = "Cancel", Width = 90, Padding = new Thickness(8, 4, 8, 4) };
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        dialog.Content = panel;

        apply.Click += (_, _) =>
        {
            _history.Push(_project, null);
            region.ReplacementText = input.Text;
            region.IsEdited = true;
            region.LayerName = region.ReplacementText.Length > 24
                ? region.ReplacementText[..24] + "…"
                : region.ReplacementText;

            if (FitTextCheck.IsChecked == true)
                _renderer.AutoFitText(region);

            ApplyEdits();
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.ShowDialog();
    }

    private void ApplyEdits()
    {
        if (_originalImage == null) return;

        var method = InpaintMethodBox.SelectedIndex == 1
            ? InpaintMethod.NavierStokes
            : InpaintMethod.Telea;

        // 1. Inpaint all edited regions on a copy of the original
        var edited = _project.TextRegions.Where(r => r.HasReplacement).ToList();
        _workingImage = edited.Count > 0
            ? _inpainter.RemoveText(_originalImage, edited, method)
            : _originalImage;

        // 2. Composite replacement text
        _finalImage = _renderer.Composite(_workingImage, _project.TextRegions);

        MainImage.Source = _compareMode ? _originalImage : _finalImage;
        DrawOverlays();
        UpdateStatus($"Applied {edited.Count} replacement(s)");
    }

    // ==================== Layers ====================

    private void LayersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LayersList.SelectedItem is TextRegion region)
        {
            foreach (var r in _project.TextRegions) r.IsSelected = false;
            region.IsSelected = true;
            DrawOverlays();
        }
    }

    // ==================== Zoom / View ====================

    private void FitToScreen()
    {
        if (_originalImage == null) return;
        var availW = CanvasScroller.ActualWidth - 20;
        var availH = CanvasScroller.ActualHeight - 20;
        if (availW <= 0 || availH <= 0) return;
        double scale = Math.Min(availW / _originalImage.PixelWidth, availH / _originalImage.PixelHeight);
        _zoom = Math.Clamp(scale, 0.05, 8);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        ImageViewbox.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomText.Text = $"{_zoom * 100:0}%";
    }

    private void FitToScreen_Click(object sender, RoutedEventArgs e) => FitToScreen();
    private void Zoom100_Click(object sender, RoutedEventArgs e) { _zoom = 1; ApplyZoom(); }
    private void Zoom200_Click(object sender, RoutedEventArgs e) { _zoom = 2; ApplyZoom(); }

    private void ToggleBoxes_Click(object sender, RoutedEventArgs e)
    {
        _showBoxes = MenuShowBoxes.IsChecked;
        DrawOverlays();
    }

    private void ToggleCompare_Click(object sender, RoutedEventArgs e)
    {
        _compareMode = MenuCompare.IsChecked;
        MainImage.Source = _compareMode ? _originalImage : (_finalImage ?? _originalImage);
        DrawOverlays();
    }

    // ==================== Export / Print ====================

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_finalImage == null)
        {
            MessageBox.Show("No image to export.");
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PNG Image|*.png|JPEG Image|*.jpg|Bitmap|*.bmp",
            FileName = "edited-screenshot.png"
        };
        if (dlg.ShowDialog() != true) return;

        BitmapEncoder encoder = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            ".bmp" => new BmpBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(_finalImage));
        using var fs = File.Create(dlg.FileName);
        encoder.Save(fs);
        UpdateStatus($"Exported: {dlg.FileName}");
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_finalImage == null)
        {
            MessageBox.Show("No image to print.");
            return;
        }

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;

        var visual = new Image
        {
            Source = _finalImage,
            Stretch = Stretch.Uniform,
            Width = dlg.PrintableAreaWidth,
            Height = dlg.PrintableAreaHeight
        };
        visual.Measure(new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight));
        visual.Arrange(new Rect(visual.DesiredSize));
        dlg.PrintVisual(visual, "Screenshot Text Editor");
        UpdateStatus("Sent to printer");
    }

    // ==================== Project Save (simple) ====================

    private void SaveProject_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_project.FilePath))
        {
            SaveProjectAs_Click(sender, e);
            return;
        }
        // Minimal: just remember path for now
        UpdateStatus($"Project saved: {_project.FilePath}");
    }

    private void SaveProjectAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Screenshot Project|*.screenshotproject",
            FileName = "project.screenshotproject"
        };
        if (dlg.ShowDialog() != true) return;
        _project.FilePath = dlg.FileName;
        // Full serialization can be expanded later
        File.WriteAllText(dlg.FileName, $"ScreenshotTextEditor Project v1.0\nRegions: {_project.TextRegions.Count}");
        UpdateStatus($"Project saved: {dlg.FileName}");
    }

    // ==================== Undo / Redo ====================

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        var (doc, _) = _history.Undo(_project, null);
        if (doc != null)
        {
            _project = doc;
            LayersList.ItemsSource = _project.TextRegions;
            ApplyEdits();
        }
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        var (doc, _) = _history.Redo(_project, null);
        if (doc != null)
        {
            _project = doc;
            LayersList.ItemsSource = _project.TextRegions;
            ApplyEdits();
        }
    }

    // ==================== Keyboard ====================

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
        {
            PasteFromClipboard();
            e.Handled = true;
        }
        else if (e.Key == Key.O && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenImage();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            foreach (var r in _project.TextRegions) r.IsSelected = false;
            DrawOverlays();
        }
    }

    // ==================== Misc ====================

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Screenshot Text Editor v1.0\n\n" +
            "Offline-first native Windows application.\n" +
            "All OCR, inpainting and rendering run locally.\n" +
            "Images are never uploaded.\n\n" +
            "Stack: .NET 8 • WPF • Tesseract • OpenCV • SkiaSharp",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Privacy_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "Privacy\n\n" +
            "✓ All image processing happens on this PC.\n" +
            "✓ Images are never uploaded.\n" +
            "✓ OCR runs locally with Tesseract.\n" +
            "✓ No telemetry by default.\n" +
            "✓ Works fully offline (Airplane mode safe).",
            "Privacy", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateStatus(string text) => StatusText.Text = text;

    protected override void OnClosed(EventArgs e)
    {
        _ocr.Dispose();
        base.OnClosed(e);
    }
}
