using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CursorMotion;

internal static class PerformanceChecks
{
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void LayeredCursor(string output)
    {
        string name = "CursorMotion.InputTransparencyTest." + Environment.ProcessId;
        Native.WndProc procedure = (window, message, parameter, data) => message switch
        { 0x84 => -1, 0x21 => 3, _ => Native.DefWindowProc(window, message, parameter, data) };
        var wc = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Proc = procedure, ClassName = name };
        Require(Native.RegisterClassEx(ref wc) != 0, "Layered class failed");
        nint window = 0;
        var hooks = new List<nint>();
        Native.WinEventProc callback = (_, _, _, _, _, _, _) => { };
        try
        {
            window = Native.CreateWindowEx(Native.CursorWindowStyle, name, "Hidden input-safe cursor", 0x80000000, -16000, -16000, 256, 200, 0, 0, 0, 0);
            Require(window != 0, "Layered cursor window failed");
            long style = Native.GetWindowLongPtr(window, -20).ToInt64();
            Require((style & 0x080800A8) == 0x080800A8 && (style & Native.NoRedirectionBitmap) == 0, "Cross-process input-safe layered style lost");
            Require(Native.SendMessage(window, 0x84, 0, 0) == -1, "Cursor intercepts hit testing");
            Require(Native.SendMessage(window, 0x21, 0, 0) == 3, "Cursor can activate");
            using var renderer = new GpuRenderer();
            Require(!renderer.UsesComposition, "Desktop cursor must use the input-safe layered presenter");
            var bounds = new Native.Rect { Left = -16000, Top = -16000, Right = -15744, Bottom = -15800 };
            int presented = 0;
            foreach (var pack in CursorPacks.All)
            foreach (int shape in new[] { 0, 1, 2 })
            {
                var frame = new RenderFrame(new(-15910, -15910), new(-15910, -15910), new(-15910, -15910), 3, 0, shape, shape, 1, .37f, 1, 1);
                renderer.Render(frame, CursorPacks.Apply(new Settings(), pack.Id), bounds);
                var pixels = renderer.Pixels(256, 200);
                Require(pixels[3] == 0, "Layered background is opaque");
                Require(pixels.Where((_, i) => i % 4 == 3).Any(alpha => alpha > 32), "Layered cursor disappeared");
                renderer.Present(window, bounds); presented++;
            }
            foreach (var range in new[] { (3u, 7u), (0x8002u, 0x8002u), (0x8004u, 0x8004u) })
            {
                nint hook = Native.SetWinEventHook(range.Item1, range.Item2, 0, callback, 0, 0, 2);
                Require(hook != 0, "Menu/notification/foreground hook failed"); hooks.Add(hook);
            }
            var policy = new TopmostPolicy();
            Require(policy.WindowEvent(1000), "Notification must raise immediately");
            Require(!policy.WindowEvent(1001), "Reorder feedback was not bounded");
            Require(policy.ShouldRaise(1002, false), "Notification burst missing");
            Require(policy.WindowEvent(1016), "Later popup event suppressed");
            File.WriteAllText(Path.Combine(output, "layered-cursor.json"), JsonSerializer.Serialize(new { hidden = true, windowStyle = style.ToString("X8"), layered = true, transparent = true, noActivate = true, presentedForms = presented, renderReadbacks = renderer.ReadbackCount, topmostHooks = hooks.Count, noDesktopInputInjected = true }));
        }
        finally
        {
            foreach (nint hook in hooks) Native.UnhookWinEvent(hook);
            if (window != 0) Native.DestroyWindow(window);
            Native.UnregisterClass(name, 0); GC.KeepAlive(procedure); GC.KeepAlive(callback);
        }
    }
    public static void Scheduling()
    {
        var settings = new Settings();
        var simulation = new Simulation();
        var policy = new FramePolicy();
        var bounds = new Native.Rect { Right = 256, Bottom = 200 };
        RenderFrame frame = default;
        int updates = 0;
        for (int index = 0; index < 1200; index++)
        {
            frame = simulation.Step(1d / 120, new(new(100, 100), true, 0, false), settings);
            if (policy.NeedsRender(frame, settings, bounds) && index > 240) updates++;
        }
        Require(updates == 0, "Stationary arrow still redraws");
        Require(simulation.ReusedSteps > 1000, "Stationary arrow still integrates its spring");
        foreach (float coordinate in new[] { -7500f, 120f, 3840f, 7500f })
        {
            var moving = new Simulation();
            for (int index = 0; index < 180; index++) moving.Step(1d / 120, new(new(coordinate + index * 2, 900), true, 0, false), settings);
            for (int index = 0; index < 1200; index++) moving.Step(1d / 120, new(new(coordinate, 900), true, 0, false), settings);
            Require(moving.ReusedSteps > 800, "Float precision prevented idle reuse at " + coordinate);
        }
        Require(policy.NeedsRender(frame, settings with { Arrow = settings.AppearanceFor(0) with { FillColor = "#FF112233" } }, bounds), "Appearance change did not invalidate cached pixels");
        policy.Invalidate(); Require(policy.NeedsRender(frame, settings, bounds), "Explicit invalidation ignored");
        int animated = 0;
        for (int index = 0; index < 180; index++)
        {
            frame = simulation.Step(1d / 120, new(new(100, 100), true, 2, false), settings);
            if (policy.NeedsRender(frame, settings, bounds)) animated++;
        }
        Require(animated == 180, "Spinner animation was frozen");
        frame = simulation.Step(1d / 120, new(new(120, 100), true, 0, true), settings);
        Require(policy.NeedsRender(frame, settings, bounds), "Movement/click/shape change ignored");
        var idle = settings with { Arrow = settings.AppearanceFor(0) with { IdleHide = .25 } };
        for (int index = 0; index < 240; index++) frame = simulation.Step(1d / 120, new(new(120, 100), true, 0, false), idle);
        Require(frame.Opacity == 0, "Idle hide stopped after settling");
        frame = simulation.Step(1d / 120, new(new(121, 100), true, 0, false), idle);
        Require(frame.Opacity > 0, "Idle hidden cursor failed to reappear");
        var paused = new Simulation();
        for (int index = 0; index < 240; index++) frame = paused.Step(1d / 120, new(new(50, 50), true, 0, false), settings);
        paused.Reset(); frame = paused.Step(1d / 120, new(new(450, 300), true, 0, false), settings);
        Require(Vector2.Distance(frame.End, new(450, 300)) < .01, "Reset reused a settled frame");
        Require(StartupRegistration.Command(@"C:\Program Files\CursorMotion\CursorMotion.exe") == "\"C:\\Program Files\\CursorMotion\\CursorMotion.exe\" --startup", "Startup path is not quoted");
    }
    public static void Composition(string output)
    {
        string name = "CursorMotion.CompositionTest." + Environment.ProcessId;
        Native.WndProc procedure = (window, message, parameter, data) => Native.DefWindowProc(window, message, parameter, data);
        var windowClass = new Native.WindowClass { Size = (uint)Marshal.SizeOf<Native.WindowClass>(), Proc = procedure, ClassName = name };
        Require(Native.RegisterClassEx(ref windowClass) != 0, "Hidden test class failed");
        nint window = Native.CreateWindowEx(Native.CompositionWindowStyle, name, "CursorMotion hidden composition test", 0x80000000, -16000, -16000, 256, 200, 0, 0, 0, 0);
        try
        {
            Require(window != 0, "Hidden test window failed");
            long exStyle = Native.GetWindowLongPtr(window, -20).ToInt64();
            Require((exStyle & Native.NoRedirectionBitmap) != 0 && (exStyle & Native.LayeredWindow) == 0, "Opaque legacy redirection surface would hide the desktop");
            Require((exStyle & 0x080000A8) == 0x080000A8, "Click-through/no-activate/tool/topmost flags lost");
            using var legacy = new GpuRenderer();
            nint incompatible = Native.CreateWindowEx(Native.LayeredWindow | 0x080000A0, name, "Legacy composition regression", 0x80000000, -16000, -16000, 1, 1, 0, 0, 0, 0);
            bool legacyRejected = false;
            try
            {
                Require(incompatible != 0, "Legacy regression window failed");
                try { legacy.AttachComposition(incompatible); }
                catch (InvalidOperationException) { legacyRejected = true; }
                Require(legacyRejected, "Legacy opaque window must be rejected before attaching a composition visual");
            }
            finally { if (incompatible != 0) Native.DestroyWindow(incompatible); }
            using var optimized = new GpuRenderer();
            optimized.AttachComposition(window);
            var bounds = new Native.Rect { Left = -16000, Top = -16000, Right = -15744, Bottom = -15800 };
            foreach (var pack in CursorPacks.All)
            foreach (int shape in new[] { 0, 1, 2 })
            {
                var settings = CursorPacks.Apply(new Settings(), pack.Id, null);
                var frame = new RenderFrame(new(-15910, -15910), new(-15910, -15910), new(-15910, -15910), 3, .1f, shape, shape, 1, .37f, 1, 1);
                legacy.Render(frame, settings, bounds);
                optimized.Render(frame, settings, bounds); optimized.Present(window, bounds);
                Require(legacy.Pixels(256, 200).SequenceEqual(optimized.Pixels(256, 200)), "Composition changed shader output");
            }
            var blur = new RenderFrame(new(-15965, -15875), new(-15920, -15990), new(-15810, -15890), 3, .16f, 0, 2, .35f, 1, 1, 128);
            legacy.Render(blur, new Settings(), bounds); optimized.Render(blur, new Settings(), bounds); optimized.Present(window, bounds);
            Require(legacy.Pixels(256, 200).SequenceEqual(optimized.Pixels(256, 200)), "Composition changed morph/curved blur output");
            long before = optimized.ReadbackCount;
            var clock = Stopwatch.StartNew();
            for (int index = 0; index < 120; index++) { optimized.Render(blur, new Settings(), bounds); optimized.Present(window, bounds); }
            double submission = clock.Elapsed.TotalMilliseconds / 120;
            Require(optimized.ReadbackCount == before, "Production composition path still reads pixels back to CPU");
            var larger = new Native.Rect { Left = -16000, Top = -16000, Right = -15640, Bottom = -15740 };
            optimized.Render(blur, new Settings(), larger); optimized.Present(window, larger);
            optimized.Render(blur, new Settings(), bounds); optimized.Present(window, bounds);
            File.WriteAllText(Path.Combine(output, "composition.json"), JsonSerializer.Serialize(new { hidden = true, experimental = true, windowStyle = exStyle.ToString("X8"), legacyRejected, shaderComparisons = CursorPacks.All.Length * 3 + 1, compositionReadbacks = optimized.ReadbackCount - before, submissionMsPerFrame = submission, samples = 128, resizing = true }));
        }
        finally { if (window != 0) Native.DestroyWindow(window); Native.UnregisterClass(name, 0); GC.KeepAlive(procedure); }
    }
    public static void Persistence(string output)
    {
        string path = Path.Combine(output, "tray-profile.json");
        var initial = new Settings();
        initial.Save(path);
        string expected;
        using (var session = new BackendSession(true, false, path))
        {
            session.Execute("set", JsonSerializer.SerializeToElement(new { property = "Fps", value = 144 }));
            session.Execute("appearance", JsonSerializer.SerializeToElement(new { shape = 0, property = "Skin", value = 1 }));
            session.Execute("appearance", JsonSerializer.SerializeToElement(new { shape = 1, property = "Scale", value = 2.35 }));
            session.Execute("flush");
            expected = JsonSerializer.Serialize(session.Execute("get"));
        }
        using var restarted = new BackendSession(true, false, path);
        Require(JsonSerializer.Serialize(restarted.Execute("get")) == expected, "Restart changed saved forms/settings");
        Require(!restarted.Engine.Enabled, "Persistence test enabled the real pointer");
        var saved = Settings.Load(path);
        Require(saved.Fps == 144 && saved.AppearanceFor(0).Skin == 1 && saved.AppearanceFor(1).Scale == 2.35 && saved.AppearanceFor(2) == initial.AppearanceFor(2), "Persisted shape independence failed");
    }
}
