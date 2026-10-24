using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class PackArtExporter
{
    public static void Export(string output)
    {
        Directory.CreateDirectory(output);
        using var gpu = new GpuRenderer();
        const int width = 320, height = 300;
        var bounds = new Native.Rect { Right = width, Bottom = height };
        var centers = new List<object>();
        foreach (var pack in CursorPacks.All)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                var s = CursorPacks.Apply(new Settings(), pack.Id);
                for (int shape = 0; shape < 3; shape++)
                {
                    Vector2 position = shape == 0 ? new(110, 35) : new(160, 150);
                    RenderFrame Frame() => new(position, position, position, 10, 0, shape, shape, 1, .37f, 1, 1);
                    var mask = s.WithAppearance(shape, s.AppearanceFor(shape) with { ShadowColor = "#00000000" });
                    gpu.Render(Frame(), mask, bounds); byte[] pixels = gpu.Pixels(width, height);
                    int left = width, right = -1, top = height, bottom = -1;
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) if (pixels[(y * width + x) * 4 + 3] > 32) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                    // Center visible ink, not the hotspot (arrow tip versus text/spinner center).
                    position += new Vector2(159.5f - (left + right) * .5f, 147.5f - (top + bottom) * .5f);
                    gpu.Render(Frame(), mask, bounds); pixels = gpu.Pixels(width, height);
                    top = height; bottom = -1;
                    for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) if (pixels[(y * width + x) * 4 + 3] > 32) { top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
                    double center = (top + bottom) * .5;
                    if (Math.Abs(center - 147.5) > 1) throw new InvalidOperationException($"Неровная миниатюра {pack.Name}/{shape}: {center}");
                    centers.Add(new { pack = pack.Name, shape, centerY = center });
                    gpu.Render(Frame(), s, bounds);
                    gpu.SavePng(Path.Combine(output, $"{pack.Id}-{shape}.png"), width, height);
                    var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, gpu.Pixels(width, height), width * 4);
                    drawing.DrawImage(image, new Rect(shape * width, 0, width, height));
                }
            }
            var result = new RenderTargetBitmap(width * 3, height, 96, 96, PixelFormats.Pbgra32); result.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(result));
            using var file = File.Create(Path.Combine(output, $"{pack.Id}.png")); encoder.Save(file);
        }
        File.WriteAllText(Path.Combine(output, "alignment.json"), JsonSerializer.Serialize(new { pass = true, centers }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
