using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CursorMotion;

internal static class SelfTest
{
    static void Require(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    public static int Run(string[] args)
    {
        int outputIndex = Array.IndexOf(args, "--output");
        string output = outputIndex >= 0 ? args[outputIndex + 1] : Path.Combine(AppContext.BaseDirectory, "self-test");
        Directory.CreateDirectory(output);
        var results = new List<object>();
        int failures = 0;
        void Test(string name, Action body)
        {
            var clock = Stopwatch.StartNew();
            try { body(); results.Add(new { name, pass = true, ms = clock.Elapsed.TotalMilliseconds }); }
            catch (Exception ex) { failures++; results.Add(new { name, pass = false, error = ex.ToString() }); }
        }
        Test("Portable startup: public EXE and quoted Unicode path without registry writes", () =>
        {
            string? before = Environment.GetEnvironmentVariable("CURSORMOTION_LAUNCHER_EXE");
            try
            {
                string exe = Path.Combine(output, "Portable app с пробелом.exe"); File.WriteAllBytes(exe, new byte[] { 0 });
                Environment.SetEnvironmentVariable("CURSORMOTION_LAUNCHER_EXE", Path.GetFullPath(exe));
                Require(AppPaths.PublicExecutable == Path.GetFullPath(exe), "Startup points to the extracted engine");
                Require(StartupRegistration.Command(AppPaths.PublicExecutable) == $"\"{Path.GetFullPath(exe)}\" --startup", "Startup path quoting lost");
                Environment.SetEnvironmentVariable("CURSORMOTION_LAUNCHER_EXE", "missing-launcher.exe");
                Require(AppPaths.PublicExecutable == Environment.ProcessPath, "Invalid launcher path accepted");
            }
            finally { Environment.SetEnvironmentVariable("CURSORMOTION_LAUNCHER_EXE", before); }
        });
        Test("Settings: validate malformed values and preserve a full profile", () =>
        {
            Settings bad = new() { Smoothing = double.NaN, Damping = -10, MaxSamples = 9000, Scale = double.PositiveInfinity, FillColor = "bad", CustomWidth = -1 };
            var valid = bad.Validate();
            Require(valid.Smoothing == 140 && valid.Damping == .3 && valid.MaxSamples == 128 && valid.Scale == 1.6 && valid.FillColor == "#FF000000" && valid.CustomWidth == 1, "Validation failed");
            string path = Path.Combine(output, "profile.json");
            var custom = new Settings { CustomX = -1920, Shutter = 720, ShadowColor = "#33445566", PreserveSpecialCursors = false };
            custom.Save(path); Require(Settings.Load(path) == custom, "Profile roundtrip changed settings");
        });
        Test("Popup ordering: immediate menu events, right-button edges and hooks", () =>
        {
            var policy = new TopmostPolicy();
            Require(policy.ShouldRaise(1000, false), "Initial raise missing");
            Require(!policy.ShouldRaise(1010, false), "Unnecessary idle raise");
            Require(policy.ShouldRaise(1011, true), "Right-button press must raise immediately");
            Require(policy.ShouldRaise(1020, false), "Right-button release must raise immediately");
            Require(policy.ShouldRaise(1300, false), "Late popup animation not covered");
            Require(!policy.ShouldRaise(1371, false), "Burst did not end");
            policy.Popup(1372); Require(policy.ShouldRaise(1372, false), "Menu event delayed");
            Native.WinEventProc callback = (_, _, _, _, _, _, _) => { };
            nint hook = Native.SetWinEventHook(4, 7, 0, callback, 0, 0, 2);
            Require(hook != 0, "Menu WinEvent hook unavailable");
            Require(Native.UnhookWinEvent(hook), "WinEvent hook cleanup failed");
            GC.KeepAlive(callback);
        });
        Test("Spring: stable across exposed smoothing/damping ranges and frame rates", () =>
        {
            foreach (double smoothing in new[] { 0d, 5, 10, 35, 140, 500 })
            foreach (double damping in new[] { .3, .85, 1, 1.5 })
            foreach (int fps in new[] { 30, 60, 144, 240 })
            {
                var sim = new Simulation(); var s = new Settings { Smoothing = smoothing, Damping = damping };
                RenderFrame f = default;
                for (int i = 0; i < fps * 6; i++)
                {
                    Vector2 raw = i < fps * 2 ? new((float)Math.Sin(i * .1) * 600, (float)Math.Cos(i * .13) * 300) : new(300, 200);
                    f = sim.Step(1d / fps, new(raw, true, 0, i % fps < fps / 3), s);
                    Require(float.IsFinite(f.End.X) && float.IsFinite(f.End.Y) && Math.Abs(f.End.X) < 4000 && Math.Abs(f.End.Y) < 4000, $"Unstable {smoothing}/{damping}/{fps}");
                    Require(f.Scale > 0 && float.IsFinite(f.Scale), "Invalid click scale");
                }
                Require(Vector2.Distance(f.End, new(300, 200)) < .1, $"Failed to settle: {smoothing}/{damping}/{fps}");
            }
        });
        Test("Blur: curved path, shutter off, click, idle and reappearance", () =>
        {
            var sim = new Simulation(); var s = new Settings { Smoothing = 0, Shutter = 720, ShutterFps = 30, IdleHide = .2 };
            RenderFrame f = default;
            for (int i = 0; i < 50; i++) f = sim.Step(1d / 120, new(new(200 + 100 * MathF.Cos(i * .12f), 200 + 100 * MathF.Sin(i * .12f)), true, 1, false), s);
            float cross = (f.Control.X - f.Start.X) * (f.End.Y - f.Start.Y) - (f.Control.Y - f.Start.Y) * (f.End.X - f.Start.X);
            Require(Math.Abs(cross) > 5 && f.Samples > 1, "Blur curve collapsed to a line");
            Vector2 end = f.End;
            for (int i = 0; i < 120; i++) f = sim.Step(1d / 120, new(end, true, 1, false), s);
            Require(f.Opacity < .001, "Idle cursor did not hide");
            f = sim.Step(1d / 120, new(new(20, 20), true, 2, true), s with { Shutter = 0 });
            Require(Vector2.Distance(f.End, new(20, 20)) < .01 && f.Samples == 1 && f.Opacity > 0, "Reappearance or disabled blur failed");
            for (int i = 0; i < 60; i++) f = sim.Step(1d / 120, new(new(20, 20), true, 2, true), s);
            Require(Math.Abs(f.Scale - s.Scale * s.ClickScale) < .01 && f.Morph == 1 && f.ShapeB == 2, "Click or shape morph failed");
        });
        Test("Monitors: negative coordinates and custom region", () =>
        {
            var d = new List<Native.Display> { new(new() { Left = -1920, Top = -100, Right = 0, Bottom = 980 }, false), new(new() { Right = 2560, Bottom = 1440 }, true) };
            var all = Native.Region(new(), d); Require(all.Left == -1920 && all.Top == -100 && all.Right == 2560 && all.Bottom == 1440, "Virtual desktop bounds failed");
            var primary = Native.Region(new() { Monitor = -2 }, d); Require(primary.Width == 2560 && primary.Left == 0, "Primary monitor failed");
            var custom = Native.Region(new() { Monitor = -1, CustomX = -400, CustomY = 20, CustomWidth = 800, CustomHeight = 600 }, d);
            Require(custom.Left == -400 && custom.Right == 400 && custom.Bottom == 620, "Custom region failed");
        });
        Test("GPU: actual HLSL compilation, shapes, alpha, curved blur and 128 samples", () =>
        {
            using var renderer = new GpuRenderer();
            File.WriteAllText(Path.Combine(output, "gpu.txt"), renderer.AdapterName);
            byte[][] masks = new byte[3][];
            Native.Rect bounds = new() { Right = 256, Bottom = 200 };
            var s = new Settings { Scale = 4, FillColor = "#99FFFFFF", OutlineColor = "#AA101020", ShadowColor = "#66000000" };
            for (int shape = 0; shape < 3; shape++)
            {
                var f = new RenderFrame(new(110, 90), new(110, 90), new(110, 90), 4, 0, shape, shape, 1, .37f, 1, 1);
                renderer.Render(f, s, bounds); masks[shape] = renderer.Pixels(256, 200);
                renderer.SavePng(Path.Combine(output, $"shape-{shape}.png"), 256, 200);
                int occupied = 0;
                for (int i = 0; i < masks[shape].Length; i += 4)
                {
                    int a = masks[shape][i + 3]; if (a > 4) occupied++;
                    Require(masks[shape][i] <= a + 1 && masks[shape][i + 1] <= a + 1 && masks[shape][i + 2] <= a + 1, "Broken premultiplied alpha");
                }
                Require(occupied > 100 && occupied < 18000, $"Empty or unbounded cursor: {shape}");
                Require(masks[shape][3] == 0, "Transparent background failed");
            }
            Require(!masks[0].SequenceEqual(masks[1]) && !masks[1].SequenceEqual(masks[2]), "Shapes are identical");
            var blur = new RenderFrame(new(35, 125), new(80, 10), new(190, 110), 3, .16f, 0, 1, .35f, 1, 1, 128);
            renderer.Render(blur, s, bounds); renderer.SavePng(Path.Combine(output, "curved-blur.png"), 256, 200);
            Require(renderer.Pixels(256, 200).Where((_, i) => i % 4 == 3).Count(a => a > 1) > 800, "Blur path missing");
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 120; i++) renderer.Render(blur, s, bounds);
            File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(new { frames = 120, msPerFrame = watch.Elapsed.TotalMilliseconds / 120, size = "256x200", samples = 128 }));
        });
        Test("GPU effects: optical sizes, maximum-scale AA and all appearance controls", () => GraphicsChecks.Run(output));
        Test("Scheduling: static cache, animated wait, clicks, edits, idle and resume", PerformanceChecks.Scheduling);
        Test("Tray profile: immediate flush, process restart and independent forms", () => PerformanceChecks.Persistence(output));
        Test("Desktop presenter: layered alpha, input-transparent flags, all forms and notification hooks", () => PerformanceChecks.LayeredCursor(output));
        Test("Experimental DirectComposition: hidden GPU presentation, unchanged pixels, no CPU readback", () => PerformanceChecks.Composition(output));
        Test("Per-shape appearance: migration, persistence, isolation, GPU output and timing", () => AppearanceChecks.Run(output));
        Test("Cursor packs: fifteen vector forms, materials, alpha, mixing and persistence", () => PackChecks.Run(output));
        Test("Native custom cursor: 120 CURs, five animations, hotspots and shell classification (no global changes)", () => NativeCursorChecks.Run(output));
        Test("Native GPU presenter: blur/morph/tilt, 80 material frames, nine role copies and large cursors (private handles)", () => NativePresenterChecks.Run(output));
        Test("Compact capture sprites: 80 GPU frames, unchanged hotspot pixels, masks, role copies and 256px limit", () => CaptureSpriteChecks.Run(output));
        Test("Hand: five materials, four independent forms, migration and bidirectional morphs", () => HandChecks.Run(output));
        Test("Resize: four directions, five materials, centered hotspots, morphs and inherited arrow profile", () => ResizeChecks.Run(output));
        Test("Glass and honest artwork: transparency, rim, Classic, GPU/PNG/Windows pixel agreement", () => GlassArtworkChecks.Run(output));
        Test("Rapid motion: bounded lag/bitmap, dense exposure, 216 high-smoothing 4K replays", () => MotionStressChecks.Run(Path.Combine(output, "motion-stress"), true));
        Test("Native temporal motion: anchored endpoint, normalized whole-cursor exposure, 40 native material/shape frames", () => AnchoredMotionChecks.Run(output));
        Test("Visible native blur: smoothing response in actual pixels and stationary click canvas/hotspot stability", () => FxReadabilityChecks.Run(Path.Combine(output, "fx-readability"), true));
        Test("Role transitions: stable family canvas/hotspot, nonempty morphs, continuous opacity at blur onset", () => TransitionChecks.Run(Path.Combine(output, "transitions"), true));
        if (!args.Contains("--offscreen"))
        {
        Test("Windows: cursor visibility API preserves GetCursorInfo shape/visibility", () =>
        {
            var before = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() }; Require(Native.GetCursorInfo(ref before), "GetCursorInfo failed");
            Require(Native.MagInitialize(), "MagInitialize failed");
            try
            {
                Require(Native.MagShowSystemCursor(false), "MagShowSystemCursor(false) failed");
                var during = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() }; Require(Native.GetCursorInfo(ref during), "GetCursorInfo failed while hidden");
                File.WriteAllText(Path.Combine(output, "cursor-api.json"), JsonSerializer.Serialize(new { beforeFlags = before.Flags, duringFlags = during.Flags, cursorBefore = before.Cursor.ToInt64(), cursorDuring = during.Cursor.ToInt64() }));
                Require(before.Flags == during.Flags, "Magnification cursor hide changes logical visibility: overlay input needs adjustment");
            }
            finally { Native.MagShowSystemCursor(true); Native.MagUninitialize(); }
        });
        Test("Watchdog: recover pointer after heartbeat timeout", () =>
        {
            using var guard = new CursorGuard();
            using var map = MemoryMappedFile.OpenExisting(guard.StateName);
            using var state = map.CreateViewAccessor();
            guard.SetHidden(true);
            var timeout = Stopwatch.StartNew();
            while (state.ReadInt32(16) == 0 && timeout.ElapsedMilliseconds < 4000) Thread.Sleep(30);
            Require(state.ReadInt32(16) == 1 && state.ReadInt32(12) == 1, "Watchdog did not restore cursor after timeout");
        });
        Test("Watchdog: recover pointer after hard process termination", () =>
        {
            string ready = Path.GetFullPath(Path.Combine(output, "guard-probe-" + Guid.NewGuid().ToString("N") + ".json"));
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { ArgumentList = { "--guard-probe", ready }, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden })!;
            try
            {
                var timeout = Stopwatch.StartNew();
                while (!File.Exists(ready) && timeout.ElapsedMilliseconds < 6000 && !child.HasExited) Thread.Sleep(30);
                Require(File.Exists(ready), "Probe did not initialize");
                using var info = JsonDocument.Parse(File.ReadAllText(ready));
                using var map = MemoryMappedFile.OpenExisting(info.RootElement.GetProperty("StateName").GetString()!);
                using var state = map.CreateViewAccessor();
                child.Kill(); child.WaitForExit(2000); timeout.Restart();
                while (state.ReadInt32(16) == 0 && timeout.ElapsedMilliseconds < 4000) Thread.Sleep(30);
                Require(state.ReadInt32(16) == 2 && state.ReadInt32(12) == 1, "Watchdog did not restore cursor after parent was killed");
            }
            finally { if (!child.HasExited) child.Kill(); CursorGuard.Restore(); }
        });
        Test("Desktop overlay: create, render, high-resolution frame timing and stop", () =>
        {
            var display = Native.Displays().First(d => d.Primary).Bounds;
            using var engine = new OverlayEngine(new Settings(), t => new(new(display.Left + display.Width / 2 + (float)Math.Sin(t * 5) * 60, display.Top + display.Height / 2 + (float)Math.Cos(t * 5) * 60), true, 0, false));
            engine.Enabled = true;
            var timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < 3500 && engine.Failure == null) Thread.Sleep(25);
            engine.Enabled = false;
            File.WriteAllText(Path.Combine(output, "desktop.json"), JsonSerializer.Serialize(new { engine.Status, engine.RenderedFrames, engine.RenderMilliseconds, engine.ActualFps, engine.Diagnostics }));
            Require(engine.Failure == null, engine.Failure ?? "Overlay failed");
            Require(engine.RenderedFrames >= 30, $"Overlay only rendered {engine.RenderedFrames} frames: {engine.Diagnostics}");
        });
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
