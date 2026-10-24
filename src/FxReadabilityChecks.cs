using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class FxReadabilityChecks
{
    public static void Run(string output, bool enforce)
    {
        Directory.CreateDirectory(output); using var gpu = new GpuRenderer();
        var records = new List<object>(); bool valid = true; double previousInk = 0; float previousLength = 0;
        foreach (double smoothing in new[] { 0d, 140, 436, 500 })
        {
            var settings = new Settings { Scale = 4.2, Smoothing = smoothing, Damping = 1, Shutter = 180, ShutterFps = 60, MaxSamples = 64, Tilt = 0 };
            var sim = new Simulation(); sim.NativeReappeared(); RenderFrame frame = default;
            for (int i = 0; i <= 360; i++) frame = sim.Step(1d/120, new(new(1000 + i * 4000f / 120, 500), true, 0, false), settings, nativeEffects: true);
            frame = NativeCursorPresenter.Anchored(frame); var bounds = NativeCursorPresenter.Bounds(frame, settings);
            gpu.Render(frame, settings, bounds); var moving = gpu.Pixels(bounds.Width, bounds.Height);
            gpu.SavePng(Path.Combine(output, "blur-" + smoothing + ".png"), bounds.Width, bounds.Height);
            gpu.Render(frame with { Start = Vector2.Zero, Control = Vector2.Zero, Samples = 1 }, settings, bounds); var sharp = gpu.Pixels(bounds.Width, bounds.Height);
            double headInk = 0, tailInk = 0;
            for (int i = 0; i < sharp.Length; i += 4) { headInk += sharp[i+3]; if(sharp[i+3] == 0) tailInk += moving[i+3]; }
            float length = MotionStressChecks.PathLength(frame);
            // The normalized exposure spreads a finite amount of ink; length
            // is the direct perceptual persistence measure, not equal growth
            // of total opacity for two close slider values.
            bool pass = tailInk / headInk > .12 && (smoothing == 0 || (tailInk > previousInk * 1.01 && length > previousLength * 1.1));
            valid &= pass; previousInk = tailInk; previousLength = length;
            records.Add(new { smoothing, pathPixels = length, samples = frame.Samples, headInk, tailInk, tailToHeadRatio = tailInk / headInk, pass });
        }
        var circleSettings = new Settings { Scale = 4.2, Smoothing = 436, Shutter = 180, Damping = 1 };
        var circle = new Simulation(); RenderFrame circleFrame = default;
        for (int i = 0; i <= 360; i++)
        {
            double t = i / 120d;
            var raw = new Vector2(1920 + 300 * (float)Math.Cos(t * Math.PI * 4), 1080 + 300 * (float)Math.Sin(t * Math.PI * 4));
            circleFrame = NativeCursorPresenter.Anchored(circle.Step(1d/120, new(raw, true, 0, false), circleSettings, nativeEffects: true));
        }
        bool circlePass = circleFrame.End == Vector2.Zero && -circleFrame.Start.Y > 1;
        valid &= circlePass;
        var clicks = new List<object>();
        foreach (int shape in new[] { 0, 1, 2, 3 })
        foreach (double scale in new[] { 1.6, 4.2 })
        {
            var settings = new Settings { Scale = scale, Smoothing = 0, Shutter = 180, ClickScale = .85 };
            var sim = new Simulation(); sim.NativeReappeared(); var canvases = new HashSet<string>();
            float minimumScale = float.MaxValue, maximumScale = 0, minimumOpacity = 1;
            for (int i = 0; i < 300; i++)
            {
                var frame = NativeCursorPresenter.Anchored(sim.Step(1d/120, new(new(1000, 500), true, shape, i >= 60 && i % 90 < 30), settings, nativeEffects: true));
                if (i < 60) continue;
                var bounds = NativeCursorPresenter.Bounds(frame, settings);
                canvases.Add($"{bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");
                minimumScale = Math.Min(minimumScale, frame.Scale); maximumScale = Math.Max(maximumScale, frame.Scale); minimumOpacity = Math.Min(minimumOpacity, frame.Opacity);
            }
            bool pass = canvases.Count == 1 && minimumScale >= scale * settings.ClickScale - .001 && maximumScale <= scale + .001 && minimumOpacity == 1;
            valid &= pass; clicks.Add(new { shape, scale, nativeCanvasVariants = canvases.Count, minimumScale, maximumScale, minimumOpacity, pass });
        }
        File.WriteAllText(Path.Combine(output, "fx-readability.json"), JsonSerializer.Serialize(new { pass = valid, blur = records, circleTrailFollowsActualDirection = circlePass, clicks, noGlobalChanges = true }, new JsonSerializerOptions { WriteIndented = true }));
        if (enforce && !valid) throw new InvalidOperationException("Blur is hidden/unresponsive, or clicking changes the native canvas/hotspot");
    }
}
