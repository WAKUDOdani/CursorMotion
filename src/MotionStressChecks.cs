using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class MotionStressChecks
{
    internal static float PathLength(RenderFrame frame) => Vector2.Distance(frame.Start, frame.Control) + Vector2.Distance(frame.Control, frame.End);
    static float SampleSpacing(RenderFrame frame)
    {
        Vector2 At(float t) => (1-t)*(1-t)*frame.Start + 2*(1-t)*t*frame.Control + t*t*frame.End;
        float gap = 0; var previous = At(.5f / frame.Samples);
        for (int i = 1; i < frame.Samples; i++) { var current = At((i + .5f) / frame.Samples); gap = Math.Max(gap, Vector2.Distance(current, previous)); previous = current; }
        return gap;
    }
    public static void Run(string output, bool enforce)
    {
        Directory.CreateDirectory(output); CheckSmoothingResponse(output, enforce); CheckIdleHistory(output, enforce); CheckExposureStops(output, enforce); var records = new List<object>();
        foreach (double smoothing in new[] { 248d, 500 })
        foreach (double damping in new[] { .3, 1d })
        foreach (int fps in new[] { 30, 120, 240 })
        foreach (double scale in new[] { .5, 1.6, 6 })
        foreach (double shutter in new[] { 180d, 720 })
        foreach (int quality in new[] { 4, 64, 128 })
        {
            var look = new CursorAppearance { Scale = scale };
            var settings = new Settings { Smoothing = smoothing, Damping = damping, Shutter = shutter, ShutterFps = shutter > 180 ? 24 : 60,
                MaxSamples = quality, Arrow = look, Text = look, Wait = look, Hand = look };
            var sim = new Simulation(); sim.NativeReappeared();
            float maxLag = 0, maxGap = 0; long maxPixels = 0; int hardFallbacks = 0;
            for (int i = 0; i < fps * 3; i++)
            {
                double t = i / (double)fps;
                var raw = i < fps / 4 ? new Vector2(1920, 1080) : new Vector2(1920 + 1750 * (float)Math.Sin(t * 21), 1080 + 940 * (float)Math.Sin(t * 27));
                var frame = sim.Step(1d / fps, new(raw, true, (i / fps) % 8, false), settings, nativeEffects: true);
                float lag = Vector2.Distance(frame.End, raw), gap = SampleSpacing(frame);
                var native = NativeCursorPresenter.Anchored(frame);
                var bounds = NativeCursorPresenter.Bounds(native, settings);
                maxLag = Math.Max(maxLag, lag); maxGap = Math.Max(maxGap, gap); maxPixels = Math.Max(maxPixels, (long)bounds.Width * bounds.Height);
                if (bounds.Width > 2048 || bounds.Height > 2048) hardFallbacks++;
                if (enforce)
                {
                    Require(float.IsFinite(frame.End.X) && float.IsFinite(frame.Control.Y), "Non-finite rapid-turn frame");
                    Require(native.End == Vector2.Zero && native.LockHead, "Actual native head encodes spring lag");
                    Require(lag <= Simulation.MaximumVisualLag((float)scale, settings) + .01, "Smoothing exceeds the native frame safety budget");
                    Require(gap <= Simulation.MaximumSampleSpacing((float)scale) + .01, "Exposure samples split into cursor clones");
                    Require(bounds.Width <= 2048 && bounds.Height <= 2048, "Rapid motion hits the hard static-frame fallback");
                }
            }
            for (int i = 0; i < fps * 5; i++)
            {
                var frame = sim.Step(1d / fps, new(new(1920, 1080), true, 0, false), settings, nativeEffects: true);
                if (enforce && i == fps * 5 - 1) Require(Vector2.Distance(frame.End, new(1920, 1080)) < .1 && frame.Samples == 1, "Rapid motion does not settle back to one sharp pointer");
            }
            records.Add(new { smoothing, damping, fps, scale, shutter, quality, maxLag, maxGap, maxPixels, hardFallbacks });
        }
        File.WriteAllText(Path.Combine(output, "motion-stress.json"), JsonSerializer.Serialize(new { enforced = enforce, records }, new JsonSerializerOptions { WriteIndented = true }));
        if (enforce)
        {
            var maximumLook = new CursorAppearance { Scale = 6, OutlineWidth = 4, ShadowBlur = 10, ShadowX = 10, ShadowY = -10 };
            var maximumSettings = new Settings { Arrow = maximumLook, Text = maximumLook, Wait = maximumLook, Hand = maximumLook,
                Smoothing = 500, Damping = 1.5, Shutter = 720, ShutterFps = 24, MaxSamples = 128, ClickScale = .5 };
            foreach (int fps in new[] { 30, 120 })
            {
                var sim = new Simulation();
                for (int i = 0; i < fps * 3; i++)
                {
                    var raw = new Vector2(1920 + 1750 * (float)Math.Sin(i * 21d / fps), 1080 + 940 * (float)Math.Sin(i * 27d / fps));
                    var frame = sim.Step(1d / fps, new(raw, true, (i / 10) % 8, i % 10 < 5), maximumSettings, nativeEffects: true);
                    var bounds = NativeCursorPresenter.Bounds(NativeCursorPresenter.Anchored(frame), maximumSettings);
                    Require(bounds.Width <= 2048 && bounds.Height <= 2048, "Largest shadows, morphs and clicks exceed the native frame budget");
                }
            }
            foreach (int fps in new[] { 30, 120, 240 })
            {
                var settings = new Settings { Smoothing = 248, Damping = 1, Shutter = 180 };
                var sim = new Simulation(); sim.NativeReappeared(); RenderFrame frame = default;
                for (int i = 0; i < fps / 3; i++) frame = sim.Step(1d / fps, new(Vector2.Zero, true, 0, false), settings);
                Vector2 final = default;
                for (int i = 1; i <= fps / 3; i++) { final = new Vector2(i * 35000f / fps, 0); frame = sim.Step(1d / fps, new(final, true, 0, false), settings); }
                for (int i = 0; i < fps * 2; i++)
                {
                    frame = sim.Step(1d / fps, new(final, true, 0, false), settings);
                    Require(frame.End.X <= final.X + .05, "Critical damping gained a spurious overshoot after the safety limit");
                }
                Require(Vector2.Distance(frame.End, final) < .01, "Fast stop did not return to the real hotspot");
            }
        }
    }
    static void CheckSmoothingResponse(string output, bool enforce)
    {
        var records = new List<object>(); bool valid = true;
        foreach (float speed in new[] { 1000f, 2500f })
        {
            float previousLag = -1;
            foreach (double smoothing in new[] { 0d, 35, 140, 248, 500 })
            {
                var settings = new Settings { Smoothing = smoothing, Damping = 1, Shutter = 0 };
                var sim = new Simulation(); sim.NativeReappeared(); RenderFrame frame = default;
                for (int i = 0; i <= 360; i++) frame = sim.Step(1d / 120, new(new(speed * i / 120, 0), true, 0, false), settings);
                float lag = speed * 3 - frame.End.X;
                // Unbounded critically damped spring ramp response: lag = 2*v/omega.
                // Allow the small integration bias; the renderer's safety guard
                // must not flatten ordinary motion across different slider values.
                double expected = speed * smoothing / 2000;
                bool pass = smoothing == 0 ? lag == 0 : lag > previousLag + 5 && Math.Abs(lag - expected) < Math.Max(speed * .0021, expected * .03);
                valid &= pass; previousLag = lag;
                records.Add(new { speed, smoothing, lag, expected, pass });
            }
        }
        File.WriteAllText(Path.Combine(output, "smoothing-response.json"), JsonSerializer.Serialize(new { pass = valid, records }, new JsonSerializerOptions { WriteIndented = true }));
        if (enforce) Require(valid, "The safety guard suppresses ordinary smoothing response");
    }
    static void CheckIdleHistory(string output, bool enforce)
    {
        var records = new List<object>(); bool valid = true;
        foreach (double smoothing in new[] { 0d, 35, 248, 500 })
        {
            var settings = new Settings { Smoothing = smoothing, Damping = 1, Shutter = 720, ShutterFps = 24, MaxSamples = 128 };
            var cached = new Simulation(); var reference = new Simulation(); float maxError = 0;
            for (int i = 0; i < 420; i++)
            {
                var raw = i < 300 ? Vector2.Zero : new Vector2((i - 299) * 200f / 120, 0);
                var input = new CursorInput(raw, true, 0, false);
                var actual = cached.Step(1d / 120, input, settings);
                // Equal settings but fresh identity disables only the idle fast
                // path, reproducing pre-optimization history maintenance.
                var expected = reference.Step(1d / 120, input, settings with { });
                if (i >= 300) maxError = Math.Max(maxError, Math.Max(Vector2.Distance(actual.Start, expected.Start), Math.Max(Vector2.Distance(actual.Control, expected.Control), Vector2.Distance(actual.End, expected.End))));
            }
            bool pass = maxError < .02 && cached.ReusedSteps > 100;
            valid &= pass; records.Add(new { smoothing, maxError, cachedSteps = cached.ReusedSteps, pass });
        }
        File.WriteAllText(Path.Combine(output, "idle-history.json"), JsonSerializer.Serialize(new { pass = valid, records }, new JsonSerializerOptions { WriteIndented = true }));
        if (enforce) Require(valid, "Idle optimization changes the resumed exposure versus uncached simulation");
    }
    static void CheckExposureStops(string output, bool enforce)
    {
        var records = new List<object>(); bool valid = true;
        foreach (int fps in new[] { 30, 120, 240 })
        foreach (double shutter in new[] { 180d, 720 })
        foreach (double smoothing in new[] { 0d, 140, 500 })
        {
            var settings = new Settings { Smoothing = smoothing, Damping = 1, Shutter = shutter, ShutterFps = shutter > 180 ? 24 : 60, Scale = 3.85 };
            var sim = new Simulation(); float overrun = 0;
            const float destination = 4000;
            for (int i = 0; i <= fps * 2; i++)
            {
                float x = Math.Min(i * destination / fps, destination);
                var frame = sim.Step(1d / fps, new(new(x, 0), true, 0, false), settings);
                if (i <= fps) continue;
                for (int n = 0; n < frame.Samples; n++)
                {
                    float t = frame.Samples > 1 ? (n + .5f) / frame.Samples : 1;
                    float sample = (1-t)*(1-t)*frame.Start.X + 2*(1-t)*t*frame.Control.X + t*t*frame.End.X;
                    overrun = Math.Max(overrun, sample - destination);
                }
            }
            bool pass = overrun < .05; valid &= pass; records.Add(new { fps, shutter, smoothing, overrun, pass });
        }
        File.WriteAllText(Path.Combine(output, "exposure-stops.json"), JsonSerializer.Serialize(new { pass = valid, records }, new JsonSerializerOptions { WriteIndented = true }));
        if (enforce) Require(valid, "Exposure curve invents motion beyond a critically damped stop");
    }
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
