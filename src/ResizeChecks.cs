using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class ResizeChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        using var gpu = new GpuRenderer();
        var drawingVisual = new DrawingVisual(); var records = new List<object>();
        using (var drawing = drawingVisual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 29, 40)), null, new Rect(0, 0, 1000, 1000));
            foreach (var (pack, row) in CursorPacks.All.Select((p, i) => (p, i)))
            {
                var settings = CursorPacks.Apply(new Settings { Smoothing = 0 }, pack.Id);
                settings = settings.WithAppearance(0, settings.AppearanceFor(0) with { ShadowColor = "#00000000", Scale = 5 });
                drawing.DrawText(new FormattedText(pack.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.White, 1), new Point(15, row * 200 + 15));
                foreach (int shape in Enumerable.Range(4, 4))
                {
                    uint role = NativeCursorPresenter.RoleForShape(shape);
                    Require(NativeCursorPresenter.ShapeFor(role) == shape && !NativeCursorPresenter.IsSpecial(role), "Resize bypasses primary renderer");
                    Require(settings.AppearanceFor(shape) == settings.AppearanceFor(0), "Resize did not inherit the arrow appearance");
                    var pos = new Vector2(90, 90);
                    var frame = new RenderFrame(pos, pos, pos, 5, 0, shape, shape, 1, 0, 1, 1);
                    gpu.Render(frame, settings, new() { Right = 180, Bottom = 180 }); var pixels = gpu.Pixels(180, 180);
                    int left = 180, right = -1, top = 180, bottom = -1, ink = 0;
                    double sumX = 0, sumY = 0, sumXY = 0;
                    for (int y = 0; y < 180; y++) for (int x = 0; x < 180; x++)
                    {
                        int offset = (y * 180 + x) * 4; byte a = pixels[offset + 3];
                        Require(pixels[offset] <= a + 1 && pixels[offset + 1] <= a + 1 && pixels[offset + 2] <= a + 1, "Resize broke premultiplied alpha");
                        // Glass bodies are intentionally translucent. Measure the
                        // visible silhouette, not only the brighter reflected rim.
                        if (a < 32) continue;
                        left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                        ink++; sumX += x + .5; sumY += y + .5; sumXY += (x + .5 - 90) * (y + .5 - 90);
                    }
                    Require(ink > 1000 && Math.Abs(sumX / ink - 90) < .3 && Math.Abs(sumY / ink - 90) < .3, $"Resize {pack.Name}/{shape} is empty or off-center: ink={ink}, center={sumX / ink:F3},{sumY / ink:F3}");
                    int width = right - left + 1, height = bottom - top + 1;
                    Require(shape switch { 4 => width > height * 1.6, 5 => height > width * 1.6, 6 => Math.Abs(width - height) <= 1 && sumXY > ink * 40, _ => Math.Abs(width - height) <= 1 && sumXY < -ink * 40 }, "Wrong resize direction or proportions");
                    var policy = new FramePolicy(); policy.NeedsRender(frame, settings, new() { Right = 180, Bottom = 180 });
                    Require(!policy.NeedsRender(frame with { Animation = 10 }, settings, new() { Right = 180, Bottom = 180 }), "Static resize redraws as spinner");
                    drawing.DrawImage(BitmapSource.Create(180, 180, 96, 96, PixelFormats.Pbgra32, null, pixels, 720), new Rect(160 + (shape - 4) * 205, row * 200 + 10, 180, 180));
                    foreach (int other in Enumerable.Range(0, 8))
                    {
                        if (shape == other) continue;
                        var sim = new Simulation(); sim.NativeReappeared();
                        for (int i = 0; i < 70; i++) sim.Step(1d / 120, new(pos, true, other, false), settings);
                        bool intermediate = false; RenderFrame f = default;
                        for (int i = 0; i < 70; i++)
                        {
                            f = sim.Step(1d / 120, new(pos, true, shape, false), settings);
                            intermediate |= f.Morph > 0 && f.Morph < 1;
                            Require(f.End == pos && f.Opacity > .99, "Resize morph moved the mouse or hid the pointer");
                        }
                        Require(intermediate && f.ShapeB == shape && f.Morph == 1, "Resize transition failed");
                    }
                    records.Add(new { material = pack.Name, shape, role, width, height, ink });
                }
            }
        }
        var bitmap = new RenderTargetBitmap(1000, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawingVisual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(output, "resize-all-packs.png"))) encoder.Save(file);
        File.WriteAllText(Path.Combine(output, "resize-results.json"), JsonSerializer.Serialize(new { pass = true, records, noGlobalChanges = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
