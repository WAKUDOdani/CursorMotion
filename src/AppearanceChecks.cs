using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class AppearanceChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        var legacy = new Settings { Version = 1, Scale = 2.4, FillColor = "#FF445566", OutlineWidth = 2.1, ShadowBlur = 4, ShadowX = -3, ShadowY = 5, MorphTime = 230, IdleHide = 2 };
        var migrated = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(legacy))!.Validate();
        for (int shape = 0; shape < 3; shape++) Require(migrated.AppearanceFor(shape) == legacy.AppearanceFor(shape), "Migration changed a legacy appearance");
        Require(migrated.Version == 3, "Legacy version was not migrated");
        Settings s = new Settings()
            .WithAppearance(0, new() { Scale = 1, FillColor = "#FFFF0000", OutlineColor = "#FFFF0000", ShadowColor = "#00000000", OutlineWidth = 0, MorphTime = 0 })
            .WithAppearance(1, new() { Scale = 2, FillColor = "#FF00FF00", OutlineColor = "#FF00FF00", ShadowColor = "#00000000", OutlineWidth = 0, MorphTime = 500 })
            .WithAppearance(2, new() { Scale = 3, FillColor = "#FF0000FF", OutlineColor = "#FF0000FF", ShadowColor = "#00000000", OutlineWidth = 0, MorphTime = 0 });
        string profile = Path.Combine(output, "three-appearances.json");
        s.Save(profile); Require(Settings.Load(profile) == s, "Separate appearances did not survive JSON roundtrip");
        var changed = s.WithAppearance(1, s.AppearanceFor(1) with { Scale = 4, ShadowX = -7, ShadowY = 8, ShadowBlur = 9, ShadowColor = "#55ABCDEF", OutlineWidth = 3.5, IdleHide = 1 });
        Require(changed.AppearanceFor(0) == s.AppearanceFor(0) && changed.AppearanceFor(2) == s.AppearanceFor(2) && changed.Style == s.Style, "Editing text altered another appearance or desktop selection");
        var invalid = changed.WithAppearance(2, new() { Scale = 100, FillColor = "invalid", ShadowX = -100, IdleHide = -5, MorphTime = 900 });
        Require(invalid.Wait!.Scale == Settings.MaximumCursorScale && invalid.Wait.FillColor == "#FF000000" && invalid.Wait.ShadowX == -10 && invalid.Wait.IdleHide == 0 && invalid.Wait.MorphTime == 500, "Appearance validation failed");
        using var gpu = new GpuRenderer();
        var sim = new Simulation();
        var pos = new Vector2(128, 110);
        int[] heights = new int[3];
        var bounds = new Native.Rect { Right = 320, Bottom = 320 };
        for (int shape = 0; shape < 3; shape++)
        {
            RenderFrame f = default;
            for (int i = 0; i < 120; i++) f = sim.Step(1d / 120, new(pos, true, shape, false), s);
            Require(f.ShapeB == shape && Math.Abs(f.Scale - (shape + 1)) < .001 && f.Morph == 1, "Auto shape did not select the matching size");
            gpu.Render(f, s, bounds); var pixels = gpu.Pixels(320, 320);
            int desired = new[] { 2, 1, 0 }[shape];
            double expected = 0, other = 0; int top = 320, bottom = -1;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                expected += pixels[i + desired];
                for (int channel = 0; channel < 3; channel++) if (channel != desired) other += pixels[i + channel];
                if (pixels[i + 3] >= 128) { top = Math.Min(top, i / 4 / 320); bottom = Math.Max(bottom, i / 4 / 320); }
            }
            Require(expected > 1000 && other == 0, "Auto shape used another appearance's color");
            heights[shape] = bottom - top + 1;
            if (shape == 1)
            {
                gpu.Render(f, s.WithAppearance(0, new() { Scale = 6, FillColor = "#FF123456", ShadowX = 9, ShadowBlur = 8 }), bounds);
                Require(gpu.Pixels(320, 320).SequenceEqual(pixels), "Text rendering depends on arrow's appearance after the transition");
            }
        }
        Require(heights[0] < heights[1] && heights[1] < heights[2], "Independent rendered sizes are not increasing");
        var transition = new Simulation();
        transition.Step(.01, new(pos, true, 0, false), s);
        var halfway = transition.Step(.1, new(pos, true, 1, false), s);
        Require(halfway.Morph > 0 && halfway.Morph < .5 && halfway.Scale > 1 && halfway.Scale < 2, "Target morph time or size interpolation failed");
        var immediate = transition.Step(.01, new(pos, true, 2, false), s);
        Require(immediate.Morph == 1 && Math.Abs(immediate.Scale - 3) < .001, "Zero morph time did not switch immediately");
        Settings idle = s.WithAppearance(1, s.AppearanceFor(1) with { IdleHide = .2, MorphTime = 0 });
        for (int shape = 0; shape < 3; shape++)
        {
            var idleSim = new Simulation(); RenderFrame f = default;
            for (int i = 0; i < 120; i++) f = idleSim.Step(1d / 120, new(pos, true, shape, false), idle);
            Require(shape == 1 ? f.Opacity == 0 : f.Opacity == 1, "Idle hide leaked to another appearance");
        }
        File.WriteAllText(Path.Combine(output, "appearance-checks.json"), JsonSerializer.Serialize(new { pass = true, renderedHeights = heights, legacyMigration = true, independentColorsAndSizes = true, independentTimingAndIdle = true, jsonRoundtrip = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
