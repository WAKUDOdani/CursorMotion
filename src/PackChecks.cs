using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class PackChecks
{
    static void Require(bool pass, string text) { if (!pass) throw new InvalidOperationException(text); }
    public static void Run(string output)
    {
        var original = new Settings { Smoothing = 35 }.WithAppearance(0, new() { Scale = 2.5, MorphTime = 210, IdleHide = 4 });
        var all = CursorPacks.Apply(original, 1);
        Require(all.Smoothing == 35 && all.AppearanceFor(0).Scale == 2.5 && all.AppearanceFor(0).MorphTime == 210 && all.AppearanceFor(0).IdleHide == 4, "Applying a pack changed size or behavior");
        for (int shape = 0; shape < 3; shape++) Require(all.AppearanceFor(shape).Skin == 1, "Whole pack did not apply to three forms");
        var mixed = CursorPacks.Apply(all, 2, 1);
        Require(CursorPacks.CurrentName(new Settings()) == "Classic", "Legacy Classic was relabeled");
        Require(CursorPacks.CurrentName(all.WithAppearance(0, all.AppearanceFor(0) with { FillColor = "#FFFF0000" })) == "Свой микс", "Repainted preset still claims its stock material");
        Require(mixed.AppearanceFor(0) == all.AppearanceFor(0) && mixed.AppearanceFor(2) == all.AppearanceFor(2) && mixed.AppearanceFor(1).Skin == 2 && CursorPacks.CurrentName(mixed) == "Свой микс", "Mixing a single form changed the rest");
        string profile = Path.Combine(output, "mixed-packs.json"); mixed.Save(profile); Require(Settings.Load(profile) == mixed, "Pack mix did not survive JSON roundtrip");
        using var gpu = new GpuRenderer();
        var visual = new DrawingVisual();
        var summaries = new List<object>();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 23, 35)), null, new Rect(0, 0, 900, CursorPacks.All.Length * 205));
            foreach (var (pack, row) in CursorPacks.All.Select((p, i) => (p, i)))
            {
                var s = CursorPacks.Apply(new Settings(), pack.Id);
                drawing.DrawText(new FormattedText(pack.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.White, 1), new Point(22, row * 205 + 15));
                for (int shape = 0; shape < 3; shape++)
                {
                    Vector2 pos = shape == 0 ? new(80, 30) : new(125, 85);
                    var frame = new RenderFrame(pos, pos, pos, 6, 0, shape, shape, 1, .37f, 1, 1);
                    var bounds = new Native.Rect { Right = 260, Bottom = 175 };
                    gpu.Render(frame, s, bounds); var pixels = gpu.Pixels(260, 175);
                    int ink = 0, partial = 0;
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte alpha = pixels[i + 3]; if (alpha > 10) ink++; if (alpha > 0 && alpha < 255) partial++;
                        Require(pixels[i] <= alpha + 1 && pixels[i + 1] <= alpha + 1 && pixels[i + 2] <= alpha + 1, $"Invalid transparency in {pack.Name}/{shape}");
                    }
                    Require(ink > 300 && ink < 25000 && partial > 30, $"Missing or aliased shape in {pack.Name}/{shape}");
                    var image = BitmapSource.Create(260, 175, 96, 96, PixelFormats.Pbgra32, null, pixels, 260 * 4);
                    drawing.DrawImage(image, new Rect(110 + shape * 260, row * 205 + 15, 260, 175));
                    summaries.Add(new { pack = pack.Name, shape, ink, partial });
                }
                // Render a mixed-material morph with curved blur at full quality.
                var blur = new RenderFrame(new(20, 140), new(110, 0), new(225, 130), 2, .2f, 0, 1, .5f, .37f, 1, 128);
                gpu.Render(blur, CursorPacks.Apply(s, 2, 1), new() { Right = 300, Bottom = 200 });
                Require(gpu.Pixels(300, 200).Where((_, i) => i % 4 == 3).Sum(b => (long)b) > 1000, "Pack transition did not render");
            }
        }
        var atlas = new RenderTargetBitmap(900, CursorPacks.All.Length * 205, 96, 96, PixelFormats.Pbgra32); atlas.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(atlas));
        using (var stream = File.Create(Path.Combine(output, "all-packs.png"))) encoder.Save(stream);
        File.WriteAllText(Path.Combine(output, "pack-results.json"), JsonSerializer.Serialize(new { pass = true, packs = 5, shapes = 15, mixAndPersistence = true, renders = summaries }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
