using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class MotionRebuildChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run(string output, bool gpuChecks = true)
    {
        Directory.CreateDirectory(output);
        NativeTrajectorySampler.CheckNativeClock();
        var history = new MotionHistory();
        Vector2 Circle(double t) => new(60 * MathF.Cos((float)t * 20), 60 * MathF.Sin((float)t * 20));
        for (int i = 0; i <= 1000; i++) history.Add(i / 1000d, Circle(i / 1000d));
        var path = history.Exposure(1, Circle(1), .05) ?? throw new InvalidOperationException("Dense history produced no exposure");
        Require(path.Points[^1] == Vector2.Zero, "Exposure delayed the physical hotspot");
        float maxError = 0;
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f;
            maxError = Math.Max(maxError, Vector2.Distance(path.At(t), Circle(.95 + .05 * t) - Circle(1)));
        }
        Require(maxError < .1f, "Dense circular history was flattened or overshot");
        Require(history.Exposure(1.2, Circle(1), .02) == null, "Stopped motion left a stale trail");
        var shortened = path.Recent(.5f);
        Require(shortened.Points[^1] == Vector2.Zero && shortened.Length > 0 && shortened.Length < path.Length,
            "Budget recovery removed the whole blur or shifted its endpoint");
        var settings = new Settings { Scale = 3.15, Smoothing = 0, Shutter = 270, ShutterFps = 60, MaxSamples = 64 };
        var pose = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 3.15f, 0, 0, 0, 1, .2f, 1, 64, true);
        var builder = new NativeMotionBuilder();
        var built = builder.Build(pose, history, 1, Circle(1), settings, 1d / 120);
        Require(built.Exposure != null && built.End == Vector2.Zero && built.Scale == pose.Scale, "New motion changed cursor pose or hotspot");
        var otherFps = new NativeMotionBuilder().Build(pose, history, 1, Circle(1), settings, 1d / 30);
        Require(built.Exposure!.Same(otherFps.Exposure), "Frame rate changed the physical exposure trajectory");
        var adjusted = builder.FitAgain(built, 400, 320, 256, 64);
        Require(adjusted.Exposure != null && adjusted.Exposure.Length > 0 && adjusted.Exposure.Length < built.Exposure!.Length,
            "Transport limit switched directly to a sharp frame");
        var policy = new NativePublicationPolicy();
        Require(policy.AllRoles(pose, settings), "First publication did not initialize every role");
        Require(!policy.AllRoles(built, settings), "Pure motion still republishes nine global roles");
        policy.Invalidate(); Require(policy.AllRoles(built, settings), "DPI/appearance change did not resynchronize roles");
        var records = new List<object>();
        if (gpuChecks)
        {
            using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
            foreach (var pack in CursorPacks.All)
            foreach (int shape in Enumerable.Range(0, 8))
            {
                var looks = CursorPacks.Apply(settings, pack.Id);
                var current = built with { ShapeA = shape, ShapeB = shape, Morph = 1 };
                var bounds = NativeCursorPresenter.Bounds(current, looks);
                gpu.Render(current, looks, bounds);
                var pixels = gpu.Pixels(bounds.Width, bounds.Height);
                int nonzero = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    Require(pixels[i] <= pixels[i + 3] && pixels[i + 1] <= pixels[i + 3] && pixels[i + 2] <= pixels[i + 3], "Motion violated premultiplied material alpha");
                    if (pixels[i + 3] > 8) nonzero++;
                }
                Require(nonzero > 20, "Whole-form exposure disappeared");
                var sprite = presenter.PrepareFrame(pixels, bounds, true, 0x10DE);
                Require(sprite != null, "Normal curved exposure exceeded the native transport");
                var handles = presenter.CreatePublication(sprite!.Pixels, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY, 1);
                try { NativeCursorChecks.VerifyWindowsPixels(handles[0], sprite); NativeCursorChecks.VerifyWindowsMask(handles[0], sprite); }
                finally { Native.DestroyCursor(handles[0]); }
                if (shape == 0) gpu.SavePng(Path.Combine(output, $"new-motion-{pack.Id}.png"), bounds.Width, bounds.Height);
                records.Add(new { pack = pack.Name, shape, nonzero, sprite.Width, sprite.Height });
            }
            byte[] RenderPixels(RenderFrame frame, Settings current)
            {
                var bounds = NativeCursorPresenter.Bounds(frame, current);
                gpu.Render(frame, current, bounds); return gpu.Pixels(bounds.Width, bounds.Height);
            }
            var faint = RenderPixels(built, settings with { BlurDensity = 0 });
            var dense = RenderPixels(built, settings with { BlurDensity = 4 });
            long faintAlpha = 0, denseAlpha = 0;
            for (int i = 3; i < faint.Length; i += 4) { faintAlpha += faint[i]; denseAlpha += dense[i]; }
            Require(denseAlpha > faintAlpha * 1.1, "Density slider does not strengthen the rendered exposure");
            var staticPose = pose with { Samples = 1 };
            Require(RenderPixels(staticPose, settings with { BlurDensity = 0 }).SequenceEqual(RenderPixels(staticPose, settings with { BlurDensity = 4 })), "Motion density altered a stationary cursor");
            foreach (var pack in CursorPacks.All)
            {
                var faded = RenderPixels(built with { Opacity = .2f }, CursorPacks.Apply(settings with { BlurDensity = 4 }, pack.Id));
                Require(Enumerable.Range(0, faded.Length / 4).All(i => faded[i * 4 + 3] <= 51), "Density overrode cursor opacity");
            }
        }
        File.WriteAllText(Path.Combine(output, "motion-rebuild.json"), JsonSerializer.Serialize(new { pass = true, maxCircleErrorPixels = maxError, physicalEndpoint = true, stoppedTrailClears = true, smoothBudgetRecovery = true, records }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
