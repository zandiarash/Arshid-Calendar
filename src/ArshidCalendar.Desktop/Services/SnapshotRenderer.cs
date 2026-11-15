using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArshidCalendar.Desktop.Services;

/// <summary>Renders a window's root element over a stand-in backdrop to a PNG (used by <c>--snapshot</c> for docs/CI).</summary>
internal static class SnapshotRenderer
{
    public static void Save(FrameworkElement root, Brush backdrop, double cornerRadius, string path, double scale = 2)
    {
        root.UpdateLayout();
        var (w, h) = (root.ActualWidth, root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(backdrop, null, new Rect(0, 0, w, h), cornerRadius, cornerRadius);
            // A VisualBrush of a right-to-left element is captured mirrored; flip it back.
            if (root.FlowDirection == FlowDirection.RightToLeft) dc.PushTransform(new MatrixTransform(-1, 0, 0, 1, w, 0));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
            if (root.FlowDirection == FlowDirection.RightToLeft) dc.Pop();
        }
        var bitmap = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
