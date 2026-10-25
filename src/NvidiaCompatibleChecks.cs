using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class NvidiaCompatibleChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        Require(NativeCursorLimits.Side(0x10DE) == 128 && NativeCursorLimits.Side(0x1002) == 256, "Capture transport limits are incorrect");
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        var records = new List<object>();
        var points = Enumerable.Range(0, 16).Select(i => new Vector2(-65 * (1 - i / 15f), 18 * MathF.Sin(i / 15f * MathF.PI))).ToArray();
        points[^1] = Vector2.Zero;
        foreach (var pack in CursorPacks.All)
        foreach (double size in new[] { .5, 3.75, 6 })
        {
            var settings = CursorPacks.Apply(new Settings { Scale = size }, pack.Id);
            for (int i = 0; i < 4; i++) settings = settings.WithAppearance(i, settings.AppearanceFor(i) with { Scale = size });
            var budget = new NativeFrameBudget(); var motion = new NativeMotionBuilder();
            foreach (int shape in Enumerable.Range(0, 8))
            foreach (float tilt in new[] { 0f, -.52f, .52f })
            {
                var frame = new RenderFrame(points[0], points[7], Vector2.Zero, (float)size, tilt, shape, shape, 1, .3f, 1, 64, true, new(points, 12.5f));
                var constrained = budget.Constrain(frame, settings, settings, 0);
                var prepared = budget.Prepare(gpu, presenter, constrained, settings, motion, true, 0x10DE);
                Require(prepared.Sprite.Width == 128 && prepared.Sprite.Height == 128, "NVIDIA received a wide native frame");
                Require(prepared.Frame.End == Vector2.Zero && prepared.Frame.Exposure is { Length: > .35f }, "Fit removed motion or shifted the hardware hotspot");
                var handles = presenter.CreatePublication(prepared.Sprite.Pixels, 128, 128, prepared.Sprite.HotX, prepared.Sprite.HotY, 1);
                try { NativeCursorChecks.VerifyWindowsPixels(handles[0], prepared.Sprite); NativeCursorChecks.VerifyWindowsMask(handles[0], prepared.Sprite); }
                finally { Native.DestroyCursor(handles[0]); }
                records.Add(new { pack = pack.Name, size, shape, tilt, effectiveScale = prepared.Frame.Scale, exposure = prepared.Frame.Exposure!.Length });
            }
            var still = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, (float)size, 0, 0, 0, 1, 0, 1, 1, true);
            var fitted = budget.Prepare(gpu, presenter, budget.Constrain(still, settings, settings, 0), settings, motion, true, 0x10DE);
            var click = budget.Prepare(gpu, presenter, budget.Constrain(still with { Scale = (float)size * .85f }, settings, settings, 0), settings, motion, true, 0x10DE);
            Require(Math.Abs(click.Frame.Scale / fitted.Frame.Scale - .85f) < .002f, "Recording size cap erased the click animation");
        }
        foreach (var pack in CursorPacks.All)
        foreach (int shape in Enumerable.Range(0, 8))
        {
            var settings = CursorPacks.Apply(new Settings { Scale = 6 }, pack.Id);
            var seed = NativeCursorFile.Render(gpu, settings, shape, .2f, 128);
            Require(seed.Width == 128 && seed.Height == 128, "Fallback slot escaped the safe transport");
            string path = Path.Combine(output, $"seed-{pack.Id}-{shape}.cur"); File.WriteAllBytes(path, NativeCursorFile.Encode(seed));
            nint cursor = Native.LoadImage(0, path, 2, 128, 128, 0x10);
            Require(cursor != 0, "Recording-safe fallback did not load");
            try { NativeCursorChecks.VerifyWindowsPixels(cursor, seed); NativeCursorChecks.VerifyWindowsMask(cursor, seed); }
            finally { Native.DestroyCursor(cursor); }
        }
        File.WriteAllText(Path.Combine(output, "nvidia-compatible.json"), JsonSerializer.Serialize(new { pass = true, nativeSide = 128, frames = records, fallbackFrames = 40, globalCursorChanged = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
