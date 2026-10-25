using System.Globalization;
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class MotionRebuildPreview
{
    internal static void Export(string output)
    {
        Directory.CreateDirectory(output);
        using var gpu = new GpuRenderer();
        var settings = new Settings { Scale = 3.15, Smoothing = 0, ShadowColor = "#66000000" };
        Vector2 a = new(-104, 32), b = new(-55, -44), c = Vector2.Zero;
        var old = new RenderFrame(a, b, c, 3.15f, .08f, 0, 0, 1, 0, 1, 64, true);
        var knots = new Vector2[16];
        for (int i = 0; i < knots.Length; i++) { float t = i / 15f; knots[i] = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c; }
        var modern = old with { Exposure = new ExposurePath(knots, 12.5f) };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(35, 38, 48)), null, new Rect(0, 0, 880, 300));
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(24, 210, 220, 255)), 1);
            for (int x = 0; x < 880; x += 20) dc.DrawLine(grid, new(x, 45), new(x, 270));
            for (int y = 50; y < 270; y += 20) dc.DrawLine(grid, new(0, y), new(880, y));
            void Draw(string label, RenderFrame frame, double x)
            {
                dc.DrawText(new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 20, Brushes.White, 1), new(x + 24, 15));
                var bounds = NativeCursorPresenter.Bounds(frame, settings); gpu.Render(frame, settings, bounds);
                var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32, null, gpu.Pixels(bounds.Width, bounds.Height), bounds.Width * 4);
                dc.DrawImage(image, new Rect(x + 230 + bounds.Left, 145 + bounds.Top, bounds.Width, bounds.Height));
            }
            Draw("Прежнее размытие", old, 0); Draw("Новое ядро", modern, 440);
            dc.DrawText(new FormattedText("Одинаковые размер, траектория и экспозиция • кадры из настоящего GPU-рендера", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.LightGray, 1), new(24, 276));
        }
        var bitmap = new RenderTargetBitmap(880, 300, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, "comparison.png")); encoder.Save(file);
    }
}
