using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class GraphicsChecks
{
    static readonly Native.Rect Tile = new() { Right = 256, Bottom = 220 };
    static RenderFrame Frame(int shape = 0, float scale = 6) => new(new(90, 55), new(90, 55), new(90, 55), scale, 0, shape, shape, 1, .37f, 1, 1);
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static byte[] Draw(GpuRenderer gpu, RenderFrame frame, Settings settings) { gpu.Render(frame, settings, Tile); return gpu.Pixels(Tile.Width, Tile.Height); }
    static double Mass(byte[] pixels) => pixels.Where((_, i) => i % 4 == 3).Sum(b => (double)b);
    static (int Width, int Height, double X, double Y, int Partial) Ink(byte[] pixels, int width)
    {
        int left = width, top = pixels.Length / 4 / width, right = -1, bottom = -1, partial = 0;
        double mass = 0, xsum = 0, ysum = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int x = i / 4 % width, y = i / 4 / width, a = pixels[i + 3];
            if (a >= 128) { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y); }
            if (a > 0 && a < 255) partial++;
            mass += a; xsum += x * a; ysum += y * a;
        }
        return (right - left + 1, bottom - top + 1, xsum / Math.Max(1, mass), ysum / Math.Max(1, mass), partial);
    }
    public static void Gallery(string output)
    {
        Directory.CreateDirectory(output);
        using var gpu = new GpuRenderer();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 23, 32)), null, new Rect(0, 0, 768, 500));
            for (int row = 0; row < 2; row++)
            for (int shape = 0; shape < 3; shape++)
            {
                float scale = row == 0 ? 1.6f : 6;
                Vector2 pos = shape == 0 ? new(94, row == 0 ? 66 : 28) : new(128, 84);
                var f = Frame(shape, scale) with { Start = pos, Control = pos, End = pos };
                gpu.Render(f, new Settings(), Tile);
                var image = BitmapSource.Create(256, 220, 96, 96, PixelFormats.Pbgra32, null, gpu.Pixels(256, 220), 256 * 4);
                drawing.DrawImage(image, new Rect(shape * 256, row * 250 + 35, 256, 220));
                var label = new FormattedText(new[] { "Стрелка", "Текст", "Ожидание" }[shape] + $" · ×{scale}", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 15, Brushes.White, 1);
                drawing.DrawText(label, new Point(shape * 256 + 22, row * 250 + 15));
            }
        }
        var bitmap = new RenderTargetBitmap(768, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, "cursor-comparison.png")); encoder.Save(file);
    }
    public static void Run(string output)
    {
        using var gpu = new GpuRenderer();
        var details = new List<object>();
        var flat = new Settings { FillColor = "#FFFFFFFF", OutlineColor = "#FFFFFFFF", ShadowColor = "#00000000", OutlineWidth = 0 };
        foreach (float scale in new[] { 1.6f, 3, 6 })
        {
            var sizes = new int[3];
            for (int shape = 0; shape < 3; shape++)
            {
                var f = Frame(shape, scale) with { Start = new(100, 90), Control = new(100, 90), End = new(100, 90) };
                var ink = Ink(Draw(gpu, f, flat), Tile.Width); sizes[shape] = ink.Height;
                Require(ink.Height > 10 && ink.Partial > 10, "Missing geometry or antialiasing");
            }
            Require(sizes[1] >= sizes[0] * .86 && sizes[1] <= sizes[0] * 1.04, $"Text size does not match arrow: {string.Join('/', sizes)} at {scale}");
            Require(sizes[2] >= sizes[0] * .86 && sizes[2] <= sizes[0] * 1.04, "Wait size does not match arrow");
            details.Add(new { scale, heights = sizes });
        }
        var basicFrame = Frame();
        byte[] baseline = Draw(gpu, basicFrame, flat);
        double mass = Mass(baseline);
        byte[] outline = Draw(gpu, basicFrame, flat with { OutlineWidth = 3 });
        Require(Mass(outline) > mass * 1.2, "Outline width has no effect");
        byte[] transparent = Draw(gpu, basicFrame with { Opacity = .5f }, flat);
        Require(Math.Abs(Mass(transparent) / mass - .5) < .01, "Opacity is incorrect");
        Require(Mass(Draw(gpu, basicFrame with { Opacity = 0 }, flat)) == 0, "Hidden cursor still visible");
        byte[] colored = Draw(gpu, basicFrame, flat with { FillColor = "#FFFF0000", OutlineColor = "#FFFF0000" });
        Require(colored.Where((_, i) => i % 4 == 2).Sum(b => (double)b) > 1000 && colored.Where((_, i) => i % 4 == 0 || i % 4 == 1).Sum(b => (double)b) == 0, "Color channels are incorrect");
        byte[] shadow = Draw(gpu, basicFrame, flat with { ShadowColor = "#99000000", ShadowX = 4, ShadowY = 4, ShadowBlur = 3 });
        Require(Mass(shadow) > mass && Ink(shadow, Tile.Width).Y > Ink(baseline, Tile.Width).Y + 1, "Shadow is missing or offset is incorrect");
        byte[] softShadow = Draw(gpu, basicFrame, flat with { ShadowColor = "#99000000", ShadowX = 4, ShadowY = 4, ShadowBlur = 8 });
        Require(!softShadow.SequenceEqual(shadow), "Shadow softness has no effect");
        Require(!Draw(gpu, basicFrame with { Tilt = .35f }, flat).SequenceEqual(baseline), "Tilt has no effect");
        var small = Ink(Draw(gpu, basicFrame with { Scale = 3 }, flat), Tile.Width);
        Require(Math.Abs(Ink(baseline, Tile.Width).Height / (double)small.Height - 2) < .08, "Scale is incorrect");
        byte[] textMask = Draw(gpu, basicFrame with { ShapeA = 1, ShapeB = 1 }, flat);
        byte[] morph = Draw(gpu, basicFrame with { ShapeA = 0, ShapeB = 1, Morph = .5f }, flat);
        Require(!morph.SequenceEqual(baseline) && !morph.SequenceEqual(textMask), "Shape morph does not interpolate");
        byte[] spinnerA = Draw(gpu, basicFrame with { ShapeA = 2, ShapeB = 2, Animation = 0 }, flat with { OutlineColor = "#00000000" });
        byte[] spinnerB = Draw(gpu, basicFrame with { ShapeA = 2, ShapeB = 2, Animation = .3f }, flat with { OutlineColor = "#00000000" });
        Require(!spinnerA.SequenceEqual(spinnerB), "Wait cursor does not animate");
        var blur = basicFrame with { Start = new(30, 140), Control = new(115, 0), End = new(210, 140), Scale = 2, Samples = 128 };
        byte[] uniform = Draw(gpu, blur, flat with { TrailFalloff = 0 });
        byte[] fading = Draw(gpu, blur, flat with { TrailFalloff = 1 });
        Require(Ink(fading, Tile.Width).X > Ink(uniform, Tile.Width).X + 10, "Trail falloff does not favor the latest position");
        Require(!Draw(gpu, blur with { Samples = 4 }, flat).SequenceEqual(uniform), "Blur quality has no effect");
        // Compare maximum-size diagonal edges against a 4x-resolution pixel-area reference.
        byte[] actual = Draw(gpu, basicFrame with { Tilt = .29f }, flat);
        const int factor = 4;
        var highBounds = new Native.Rect { Right = Tile.Width * factor, Bottom = Tile.Height * factor };
        var highFrame = basicFrame with { Start = basicFrame.Start * factor, Control = basicFrame.Control * factor, End = basicFrame.End * factor, Scale = basicFrame.Scale * factor, Tilt = .29f };
        gpu.Render(highFrame, flat, highBounds); byte[] reference = gpu.Pixels(highBounds.Width, highBounds.Height);
        double error = 0; int edges = 0;
        for (int y = 0; y < Tile.Height; y++) for (int x = 0; x < Tile.Width; x++)
        {
            double average = 0;
            for (int dy = 0; dy < factor; dy++) for (int dx = 0; dx < factor; dx++) average += reference[((y * factor + dy) * highBounds.Width + x * factor + dx) * 4 + 3];
            average /= factor * factor;
            int a = actual[(y * Tile.Width + x) * 4 + 3];
            if ((average > 0 && average < 255) || (a > 0 && a < 255)) { error += Math.Abs(average - a) / 255; edges++; }
        }
        double edgeError = error / Math.Max(1, edges);
        Require(edges > 50 && edgeError < .12, $"Maximum-scale edge coverage is inaccurate: {edgeError}");
        File.WriteAllText(Path.Combine(output, "effects-details.json"), JsonSerializer.Serialize(new { opticalSizes = details, maxScaleEdgeError = edgeError, checks = "size, AA, outline, opacity, hidden, color, shadow, softness, tilt, scale, morph, spinner, curved blur, falloff, quality" }, new JsonSerializerOptions { WriteIndented = true }));
        Gallery(output);
    }
}
