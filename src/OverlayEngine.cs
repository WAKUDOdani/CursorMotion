using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CursorMotion;

internal sealed class OverlayEngine : IDisposable
{
    readonly Thread thread;
    readonly ManualResetEventSlim cancel = new();
    volatile Settings settings;
    volatile bool enabled;
    readonly Func<double, CursorInput>? inputProvider;
    readonly bool workerStarted;
    public bool Enabled { get => enabled; set => enabled = value; }
    public Settings Settings { get => settings; set => settings = RuntimeSettings(value); }
    static Settings RuntimeSettings(Settings value)
    {
        var valid = value.Validate();
        return valid with { Arrow = valid.AppearanceFor(0), Text = valid.AppearanceFor(1), Wait = valid.AppearanceFor(2), Hand = valid.AppearanceFor(3) };
    }
    public string Status { get; private set; } = "Подготовка Direct3D…";
    public string? Failure { get; private set; }
    public double RenderMilliseconds { get; private set; }
    public long RenderedFrames { get; private set; }
    public long NativeBudgetMisses { get; private set; }
    public int PeakCursorWidth { get; private set; }
    public int PeakCursorHeight { get; private set; }
    public double ActualFps { get; private set; }
    public string Diagnostics { get; private set; } = "Starting";
    public string Presentation { get; private set; } = "Starting";
    public bool CompactCursorRequired { get; private set; }
    public OverlayEngine(Settings initial, Func<double, CursorInput>? inputProvider = null, bool startThread = true)
    {
        settings = RuntimeSettings(initial); this.inputProvider = inputProvider;
        thread = new(Run) { Name = "CursorMotion GPU", IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        workerStarted = startThread;
        if (startThread) thread.Start();
    }
    void Run()
    {
        nint timer = 0;
        CursorGuard? guard = null;
        try
        {
            using var renderer = new GpuRenderer();
            CompactCursorRequired = renderer.RequiresCompactNativeCursor;
            using var presenter = new NativeCursorPresenter();
            timer = Native.CreateWaitableTimerEx(0, null, 2, 0x1F0003);
            if (timer == 0) throw Native.Error("CreateWaitableTimerEx — требуется Windows 10 версии 1803 или новее");
            guard = new();
            var nativeTheme = new NativeCursorTheme(guard);
            var simulation = new Simulation();
            var framePolicy = new FramePolicy();
            var displays = Native.Displays();
            Settings? regionSettings = null;
            Native.Rect region = default;
            bool previousEnabled = false, desktop = true, previousVisible = false;
            int themeGeneration = -1;
            uint previousRole = 0;
            float previousFootprint = 1;
            long lastDisplays = 0;
            long lastDescribe = 0; string cursorDescription = "";
            var clock = Stopwatch.StartNew();
            double previous = clock.Elapsed.TotalSeconds;
            double fpsStart = previous; long fpsFrames = 0;
            Status = renderer.AdapterName;
            Presentation = "GPU frames in Windows system cursor";
            while (!cancel.IsSet)
            {
                long tick = Environment.TickCount64;
                guard.Heartbeat();
                Settings s = settings;
                if (tick - lastDisplays > 1000)
                {
                    displays = Native.Displays(); desktop = Native.OnInputDesktop(); lastDisplays = tick; regionSettings = null;
                    presenter.RefreshRoles();
                }
                bool active = enabled && desktop;
                if (active != previousEnabled)
                {
                    simulation.Reset(); framePolicy.Invalidate(); previousEnabled = active;
                    previousVisible = false; previousRole = 0;
                }
                double now = clock.Elapsed.TotalSeconds, dt = now - previous; previous = now;
                if (!active)
                {
                    Diagnostics = $"enabled={enabled}; desktop={desktop}";
                    nativeTheme.Restore(); cancel.Wait(25); continue;
                }
                renderer.NativePixelFootprint = 1;
                nativeTheme.Ensure(s, renderer);
                if (themeGeneration != nativeTheme.Generation)
                {
                    presenter.RefreshRoles(); themeGeneration = nativeTheme.Generation;
                    framePolicy.Invalidate(); previousRole = 0;
                }
                var ci = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
                bool ok = Native.GetCursorInfo(ref ci);
                CursorInput? supplied = inputProvider?.Invoke(now);
                uint role = presenter.RoleFor(ci.Cursor);
                if (ok && role == 0) { presenter.RefreshRoles(); role = presenter.RoleFor(ci.Cursor); }
                int shape = NativeCursorPresenter.ShapeFor(role);
                if (supplied is { } input)
                {
                    ci.Position = new((int)input.Position.X, (int)input.Position.Y);
                    ci.Flags = input.Visible ? 1 : 0; role = NativeCursorPresenter.RoleForShape(input.Shape);
                    shape = input.Shape; ok = true;
                }
                if (!ReferenceEquals(regionSettings, s)) { region = Native.Region(s, displays); regionSettings = s; }
                bool inside = ok && region.Contains(ci.Position) && displays.Any(d => d.Bounds.Contains(ci.Position));
                bool special = role == 0 || (s.PreserveSpecialCursors && s.Style == -1 && NativeCursorPresenter.IsSpecial(role));
                bool visible = ok && (ci.Flags & 1) != 0 && !special;
                if (visible && !previousVisible) simulation.NativeReappeared();
                previousVisible = visible;
                bool pressed = supplied?.Pressed ?? (Native.GetAsyncKeyState(1) < 0 || Native.GetAsyncKeyState(2) < 0);
                var raw = new Vector2(ci.Position.X, ci.Position.Y);
                var simulated = simulation.Step(dt, new(raw, visible && inside, shape, pressed), s, nativeEffects: true);
                if (visible)
                {
                    float footprint = CursorRasterQuality.ForMonitor(displays, ci.Position);
                    if (footprint != previousFootprint) { framePolicy.Invalidate(); previousFootprint = footprint; }
                    renderer.NativePixelFootprint = footprint;
                    // The head stays on Windows' real hotspot continuously.
                    // The spring filters exposure and tilt, not the native image position.
                    var frame = NativeCursorPresenter.Anchored(simulated);
                    if (!inside) frame = StaticFrame(s, shape, (float)now);
                    var bounds = NativeCursorPresenter.Bounds(frame, s);
                    bool limited = bounds.Width > 2048 || bounds.Height > 2048;
                    if (limited) { frame = StaticFrame(s, shape, (float)now); bounds = NativeCursorPresenter.Bounds(frame, s); }
                    if (role != previousRole) framePolicy.Invalidate();
                    if (framePolicy.NeedsRender(frame, s, bounds))
                    {
                        double start = clock.Elapsed.TotalMilliseconds;
                        renderer.Render(frame, s, bounds);
                        bool published = presenter.Present(renderer, bounds, role, s.CaptureCompatibility);
                        PeakCursorWidth = Math.Max(PeakCursorWidth, presenter.RequiredWidth);
                        PeakCursorHeight = Math.Max(PeakCursorHeight, presenter.RequiredHeight);
                        if (!published)
                        {
                            NativeBudgetMisses++;
                            // Keep the full-size glyph when an extreme blur exceeds
                            // the capture budget. Never publish a >256px cursor here.
                            var still = StaticFrame(s, shape, (float)now);
                            var stillBounds = NativeCursorPresenter.Bounds(still, s);
                            renderer.Render(still, s, stillBounds);
                            if (!presenter.Present(renderer, stillBounds, role, true))
                                throw new InvalidOperationException("Для записи NVIDIA уменьшите размер или тень курсора.");
                        }
                        RenderMilliseconds = RenderMilliseconds * .9 + (clock.Elapsed.TotalMilliseconds - start) * .1;
                        RenderedFrames++; fpsFrames++;
                    }
                    previousRole = role;
                    Presentation = limited ? "Windows system cursor (extreme motion bounds limited)" : (s.CaptureCompatibility || CompactCursorRequired)
                        ? "GPU system cursor; compact capture frames <=256px" : "GPU frames in Windows system cursor";
                }
                else
                {
                    // The owning application/Windows controls native typing visibility.
                    // No global hide/show filter, no cursor HWND, no shell hit-test switch.
                    framePolicy.Invalidate(); previousRole = 0;
                    Presentation = special ? "Application-owned/special native cursor" : "Windows-hidden native cursor";
                }
                if (tick - lastDescribe > 500) { cursorDescription = presenter.Describe(ci.Cursor); lastDescribe = tick; }
                Diagnostics = $"cursorOk={ok}; flags={ci.Flags}; role={role}; inside={inside}; special={special}; x={ci.Position.X}; y={ci.Position.Y}; cursor={ci.Cursor:X}; shape={simulated.ShapeA}>{simulated.ShapeB}; morph={simulated.Morph:F4}; head=anchored; smoothing=effects-only; {cursorDescription}; presentation={Presentation}";
                if (now - fpsStart > 1) { ActualFps = fpsFrames / (now - fpsStart); fpsFrames = 0; fpsStart = now; }
                double remaining = 1000.0 / s.Fps - (clock.Elapsed.TotalSeconds - now) * 1000;
                if (remaining > 0)
                {
                    long due = -(long)(remaining * 10000);
                    if (!Native.SetWaitableTimer(timer, ref due, 0, 0, 0, false)) throw Native.Error("SetWaitableTimer");
                    Native.WaitForSingleObject(timer, 50);
                }
            }
        }
        catch (Exception ex) { enabled = false; Failure = ex.Message; Status = "Эффект остановлен"; Program.Log(ex); }
        finally
        {
            guard?.Dispose();
            if (timer != 0) Native.CloseHandle(timer);
        }
    }
    static RenderFrame StaticFrame(Settings settings, int detectedShape, float animation)
    {
        int shape = settings.Style >= 0 ? settings.Style : detectedShape;
        return new(Vector2.Zero, Vector2.Zero, Vector2.Zero, (float)settings.AppearanceFor(shape).Scale, 0, shape, shape, 1, animation, 1, 1);
    }
    public void Dispose()
    {
        enabled = false; cancel.Set();
        if (!workerStarted) { cancel.Dispose(); return; }
        if (!thread.Join(3500)) CursorGuard.Restore();
        if (!thread.IsAlive) cancel.Dispose();
    }
}
