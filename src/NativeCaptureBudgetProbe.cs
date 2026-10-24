using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

// Read-only/offscreen size search using the user's exact materials. Never
// publishes a cursor or changes the profile. Dynamic extremes remain bounded
// by the production fallback; the motion scenarios use specified 24px and 48px exposures.
internal static class NativeCaptureBudgetProbe
{
    internal static void Run(string profile, string output)
    {
        var original = JsonSerializer.Deserialize<Settings>(File.ReadAllText(profile))?.Validate()
            ?? throw new InvalidDataException("Empty profile");
        Directory.CreateDirectory(output);
        using var renderer = new GpuRenderer();
        using var presenter = new NativeCursorPresenter();
        var rows = new List<object>();
        bool Fits(double scale, int scenario)
        {
            var settings = original.WithAppearance(0, original.AppearanceFor(0) with { Scale = scale });
            foreach (float footprint in new[] { 1f, 1.5f })
            for (int shape = 0; shape < 8; shape++)
            for (int step = 0; step < (scenario == 0 ? 1 : scenario == 1 ? 7 : 8); step++)
            {
                renderer.NativePixelFootprint = footprint;
                Vector2 start = Vector2.Zero; float tilt = 0;
                if (scenario == 1) tilt = (step - 3) * MathF.PI / 18;
                if (scenario >= 2)
                {
                    var direction = new Vector2(MathF.Cos(step * MathF.PI / 4), MathF.Sin(step * MathF.PI / 4));
                    float exposurePixels = scenario == 3 ? 48 : 24;
                    start = -direction * exposurePixels;
                    double shutter = Math.Max(original.Shutter / 360 / original.ShutterFps, .001);
                    tilt = Math.Clamp(direction.X * exposurePixels / (float)shutter / 1000 * (float)(original.Tilt * Math.PI / 180), -MathF.PI / 6, MathF.PI / 6);
                }
                // Full candidate scale for every form is a conservative envelope
                // for morphs from the large arrow to the other, smaller forms.
                var frame = new RenderFrame(start, start / 2, Vector2.Zero, (float)scale, tilt, shape, shape, 1, .37f, 1, scenario >= 2 ? 64 : 1, true);
                var bounds = NativeCursorPresenter.Bounds(frame, settings);
                renderer.Render(frame, settings, bounds);
                if (presenter.PrepareFrame(renderer.Pixels(bounds.Width, bounds.Height), bounds, true, 0x10DE, 128) == null) return false;
            }
            return true;
        }
        foreach (int scenario in new[] { 0, 1, 2, 3 })
        {
            int low = 50, high = 600;
            while (low < high)
            {
                int mid = (low + high + 1) / 2;
                if (Fits(mid / 100d, scenario)) low = mid; else high = mid - 1;
            }
            bool accepted = Fits(low / 100d, scenario), nextRejected = !Fits((low + 1) / 100d, scenario);
            rows.Add(new { scenario = scenario == 0 ? "static-all-forms" : scenario == 1 ? "tilt-all-forms-30deg" : scenario == 2 ? "24px-exposure-all-directions" : "48px-exposure-all-directions", scale = low / 100d, accepted, nextRejected });
        }
        File.WriteAllText(Path.Combine(output, "size-limits.json"), JsonSerializer.Serialize(new { nativeSide = 128, profileChanged = false, globalCursorChanged = false, limits = rows }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
