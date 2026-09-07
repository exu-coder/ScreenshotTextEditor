using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ScreenshotTextEditor.Models;

namespace ScreenshotTextEditor.ImageProcessing;

public class InpaintingService
{
    public BitmapSource RemoveText(BitmapSource source, IEnumerable<TextRegion> regions, InpaintMethod method = InpaintMethod.Telea)
    {
        using var mat = BitmapSourceToMat(source);
        using var mask = new Mat(mat.Rows, mat.Cols, MatType.CV_8UC1, Scalar.All(0));

        foreach (var region in regions.Where(r => r.HasReplacement || r.IsEdited))
        {
            var box = region.BoundingBox;
            int pad = 2;
            int x = Math.Max(0, (int)box.X - pad);
            int y = Math.Max(0, (int)box.Y - pad);
            int w = Math.Min(mat.Cols - x, (int)box.Width + pad * 2);
            int h = Math.Min(mat.Rows - y, (int)box.Height + pad * 2);

            if (w > 0 && h > 0)
                Cv2.Rectangle(mask, new OpenCvSharp.Rect(x, y, w, h), Scalar.All(255), -1);
        }

        using var result = new Mat();
        double radius = 5.0;
        var flags = method == InpaintMethod.NavierStokes
            ? OpenCvSharp.InpaintMethod.NS
            : OpenCvSharp.InpaintMethod.Telea;

        Cv2.Inpaint(mat, mask, result, radius, flags);
        return MatToBitmapSource(result);
    }

    public BitmapSource RemoveSingleRegion(BitmapSource source, TextRegion region, InpaintMethod method = InpaintMethod.Telea)
        => RemoveText(source, new[] { region }, method);

    private static Mat BitmapSourceToMat(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        ms.Position = 0;
        using var bmp = new Bitmap(ms);
        return BitmapConverter.ToMat(bmp);
    }

    private static BitmapSource MatToBitmapSource(Mat mat)
    {
        using var bmp = BitmapConverter.ToBitmap(mat);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var decoder = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}

public enum InpaintMethod
{
    Telea,
    NavierStokes
}
