using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class HandChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        var original = CursorPacks.Apply(new Settings { Smoothing = 0, Version = 2 }, 2) with { Hand = null };
        var migrated = original.Validate();
        Require(migrated.AppearanceFor(3) == original.AppearanceFor(0), "Legacy hand does not inherit arrow material/size");
        for (int shape = 0; shape < 3; shape++) Require(migrated.AppearanceFor(shape) == original.AppearanceFor(shape), "Hand migration changed an old form");
        var edited = migrated.WithAppearance(3, CursorPacks.Find(4).Appearance(3) with { Scale = 2.75, MorphTime = 230 });
        for (int shape = 0; shape < 3; shape++) Require(edited.AppearanceFor(shape) == migrated.AppearanceFor(shape), "Hand edits leaked into another appearance");
        string path = Path.Combine(output, "four-forms.json"); edited.Save(path);
        Require(Settings.Load(path) == edited.Validate(), "Hand profile did not roundtrip");
        Require(CursorPacks.CurrentName(edited) == "Свой микс", "Mixed hand material ignored");
        Require(NativeCursorPresenter.ShapeFor(32649) == 3 && !NativeCursorPresenter.IsSpecial(32649), "Windows hand still bypasses the renderer");
        // Reversals must preserve the visible blend, on BOTH sides of halfway.
        foreach (int target in new[] { 1, 3 })
        foreach (int progress in new[] { 4, 20 })
        {
            var reversal = new Simulation();
            var settings = new Settings { Smoothing = 0, MorphTime = 240 };
            for (int i = 0; i < 40; i++) reversal.Step(1d / 120, new(Vector2.Zero, true, 0, false), settings);
            RenderFrame before = default;
            for (int i = 0; i < progress; i++) before = reversal.Step(1d / 120, new(Vector2.Zero, true, target, false), settings);
            var after = reversal.Step(.000001, new(Vector2.Zero, true, 0, false), settings);
            Require(after.ShapeA == target && after.ShapeB == 0 && Math.Abs((1 - after.Morph) - before.Morph) < .0001, "Interrupted morph jumped to an endpoint");
        }
        foreach (int target in new[] { 1, 3 })
        {
            var sim = new Simulation(); var settings = new Settings { Smoothing = 0, MorphTime = 140 };
            for (int i = 0; i < 30; i++) sim.Step(1d / 120, new(Vector2.Zero, true, 0, false), settings);
            float weight = 0;
            for (int i = 0; i < 100; i++)
            {
                // A one-frame arrow between text/hand requests reproduces the app's
                // rapid native role changes without moving or delaying the mouse.
                int shape = i % 7 == 6 ? 0 : target;
                var frame = sim.Step(1d / 120, new(Vector2.Zero, true, shape, false), settings);
                float next = frame.ShapeB == target ? frame.Morph : frame.ShapeA == target ? 1 - frame.Morph : 0;
                Require(Math.Abs(next - weight) < .1, "Rapid native role changes made the visible shape flash");
                weight = next;
            }
        }
        using var gpu = new GpuRenderer();
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(24, 29, 40)), null, new Rect(0, 0, 1000, 1000));
            foreach (var (pack, row) in CursorPacks.All.Select((pack, row) => (pack, row)))
            {
                var settings = CursorPacks.Apply(new Settings { Smoothing = 0 }, pack.Id);
                Require(settings.AppearanceFor(3).Skin == pack.Id, "Whole pack omitted the hand");
                drawing.DrawText(new FormattedText(pack.Name, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 18, Brushes.White, 1), new Point(20, row * 200 + 18));
                for (int shape = 0; shape < 4; shape++)
                {
                    var pos = shape is 0 or 3 ? new Vector2(75, 25) : new Vector2(100, 80);
                    var frame = new RenderFrame(pos, pos, pos, 5, 0, shape, shape, 1, .37f, 1, 1);
                    gpu.Render(frame, settings, new() { Right = 200, Bottom = 160 });
                    byte[] pixels = gpu.Pixels(200, 160);
                    Require(pixels.Where((_, i) => i % 4 == 3).Count(a => a > 32) > 250, "Empty hand/material form");
                    if (shape == 3)
                    {
                        for (int i = 0; i < pixels.Length; i += 4) Require(pixels[i] <= pixels[i + 3] + 1 && pixels[i + 1] <= pixels[i + 3] + 1 && pixels[i + 2] <= pixels[i + 3] + 1, "Hand broke premultiplied alpha");
                        var policy = new FramePolicy(); policy.NeedsRender(frame, settings, new() { Right = 200, Bottom = 160 });
                        Require(!policy.NeedsRender(frame with { Animation = 10 }, settings, new() { Right = 200, Bottom = 160 }), "Static hand schedules spinner redraws");
                    }
                    drawing.DrawImage(BitmapSource.Create(200, 160, 96, 96, PixelFormats.Pbgra32, null, pixels, 800), new Rect(160 + shape * 205, row * 200 + 24, 200, 160));
                }
                foreach (int from in new[] { 0, 1, 2 })
                foreach (bool reverse in new[] { false, true })
                {
                    int source = reverse ? 3 : from, target = reverse ? from : 3;
                    var sim = new Simulation(); sim.NativeReappeared();
                    for (int i = 0; i < 40; i++) sim.Step(1d / 120, new(Vector2.Zero, true, source, false), settings);
                    bool sawIntermediate = false;
                    RenderFrame f = default;
                    for (int i = 0; i < 40; i++)
                    {
                        f = sim.Step(1d / 120, new(Vector2.Zero, true, target, i < 4), settings);
                        sawIntermediate |= f.Morph > 0 && f.Morph < 1;
                        Require(float.IsFinite(f.Scale) && f.Opacity > .99, "Hand morph hid the pointer or changed click position");
                    }
                    Require(sawIntermediate && f.ShapeB == target && f.Morph == 1, "Arrow/text/wait to hand did not morph smoothly");
                }
            }
        }
        var image = new RenderTargetBitmap(1000, 1000, 96, 96, PixelFormats.Pbgra32); image.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(Path.Combine(output, "four-forms-all-packs.png"))) encoder.Save(stream);
    }
    public static void ExportArt(string directory)
    {
        using var gpu = new GpuRenderer(); var metadata = new Dictionary<string, object>();
        foreach (var pack in CursorPacks.All)
        {
            string material = pack.Name.ToLowerInvariant(), folder = Path.Combine(directory, material);
            Directory.CreateDirectory(folder);
            var settings = CursorPacks.Apply(new Settings(), pack.Id);
            var point = new Vector2(600, 220);
            var frame = new RenderFrame(point, point, point, 38, 0, 3, 3, 1, 0, 1, 1);
            gpu.Render(frame, settings, new() { Right = 1254, Bottom = 1254 });
            gpu.SavePng(Path.Combine(folder, "hand.png"), 1254, 1254);
            gpu.Render(frame, settings.WithAppearance(3, settings.AppearanceFor(3) with { ShadowColor = "#00000000" }), new() { Right = 1254, Bottom = 1254 });
            byte[] pixels = gpu.Pixels(1254, 1254); int left = 1254, top = 1254, right = 0, bottom = 0;
            for (int y = 0; y < 1254; y++) for (int x = 0; x < 1254; x++) if (pixels[(y * 1254 + x) * 4 + 3] > 64)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
            metadata[material] = new { width = 1254, height = 1254, bounds = new { left, top, right, bottom, sampleStep = 1, alphaThreshold = 64 } };
        }
        File.WriteAllText(Path.Combine(directory, "hand-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }
}
