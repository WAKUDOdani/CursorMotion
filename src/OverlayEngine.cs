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
    public string RasterDiagnostics { get; private set; } = "baseline";
    public double CursorCreateMs { get; private set; }
    public double CursorSetMs { get; private set; }
    public string MotionKernel { get; private set; } = "legacy-quadratic";
    public long HistorySamples { get; private set; }
    public string? HistoryError { get; private set; }
    public long ActiveRoleFrames { get; private set; }
    public long FamilyFrames { get; private set; }
    public int NativeCanvasSide { get; private set; } = 256;
    public float NativeScaleRatio { get; private set; } = 1;
    public float NativeMaximumScale { get; private set; } = 2.8f;
    public float NativeGeometryRatio { get; private set; } = 1;
    public long FittedFrames { get; private set; }
    public float ExposurePixels { get; private set; }
    public float ExposureMilliseconds { get; private set; }
    public double ActualFps { get; private set; }
    public string Diagnostics { get; private set; } = "Starting";
    public string Presentation { get; private set; } = "Starting";
    public bool CompactCursorRequired { get; private set; }
    public OverlayEngine(Settings initial, Func<double, CursorInput>? inputProvider = null, bool startThread = true)
    {
        settings = RuntimeSettings(initial); this.inputProvider = inputProvider;
        MotionKernel = Environment.GetEnvironmentVariable("CURSORMOTION_NEW_BLUR") == "0" ? "legacy-quadratic" : "dense-exposure-v1";
        thread = new(Run) { Name = "CursorMotion GPU", IsBackground = true };
        thread.SetApartmentState(ApartmentState.MTA);
        workerStarted = startThread;
        if (startThread) thread.Start();
    }
    void Run()
    {
        nint timer = 0;
        CursorGuard? guard = null;
        CursorTargetRaster.DpiScope? monitorRasterContext = null;
        try
        {
            using var renderer = new GpuRenderer();
            CompactCursorRequired = renderer.RequiresCompactNativeCursor;
            NativeCanvasSide = NativeCursorLimits.Side(renderer.AdapterVendorId);
            using var presenter = new NativeCursorPresenter();
            timer = Native.CreateWaitableTimerEx(0, null, 2, 0x1F0003);
            if (timer == 0) throw Native.Error("CreateWaitableTimerEx — требуется Windows 10 версии 1803 или новее");
            guard = new();
            var nativeTheme = new NativeCursorTheme(guard);
            var simulation = new Simulation();
            var framePolicy = new FramePolicy();
            bool newMotion = Environment.GetEnvironmentVariable("CURSORMOTION_NEW_BLUR") != "0";
            MotionKernel = newMotion ? "dense-exposure-v1" : "legacy-quadratic";
            using var trajectory = newMotion ? new NativeTrajectorySampler() : null;
            var motionBuilder = new NativeMotionBuilder(); var publicationPolicy = new NativePublicationPolicy();
            var frameBudget = new NativeFrameBudget();
            var sizeTransport = new NativeSizeTransport();
            var displays = Native.Displays();
            bool fixedNativeContext = CursorTargetRaster.Enabled && CursorTargetRaster.NativeCoordinates;
            Task<List<Native.Display>>? displayRefresh = null;
            Settings? regionSettings = null;
            Native.Rect region = default;
            bool previousEnabled = false, desktop = true, previousVisible = false;
            int themeGeneration = -1;
            uint previousRole = 0;
            float previousFootprint = 1;
            uint previousRasterDpi = 0;
            Settings? rasterSource = null; Settings? rasterGeometry = null; float geometryRatio = 1;
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
                NativeCanvasSide = sizeTransport.Select(renderer, presenter, s);
                if (displayRefresh is { IsCompleted: true })
                {
                    displays = displayRefresh.GetAwaiter().GetResult(); displayRefresh = null;
                    regionSettings = null; presenter.RefreshRoles();
                }
                if (tick - lastDisplays > 1000)
                {
                    if (fixedNativeContext) displayRefresh ??= Task.Run(Native.PhysicalDisplays);
                    else displays = Native.Displays();
                    desktop = Native.OnInputDesktop(); lastDisplays = tick; regionSettings = null;
                    presenter.RefreshRoles();
                }
                bool active = enabled && desktop;
                if (trajectory != null)
                {
                    trajectory.Enabled = active;
                    HistorySamples = trajectory.Samples;
                    if (HistoryError == null && trajectory.Failure is { } historyFailure)
                    {
                        HistoryError = historyFailure.Message; Program.Log(historyFailure);
                        MotionKernel = "legacy-quadratic (history fallback)";
                    }
                }
                if (active != previousEnabled)
                {
                    simulation.Reset(); framePolicy.Invalidate(); previousEnabled = active;
                    publicationPolicy.Invalidate();
                    previousVisible = false; previousRole = 0;
                }
                double now = clock.Elapsed.TotalSeconds, dt = now - previous; previous = now;
                if (!active)
                {
                    monitorRasterContext?.Dispose(); monitorRasterContext = null;
                    Diagnostics = $"enabled={enabled}; desktop={desktop}";
                    nativeTheme.Restore(); cancel.Wait(25); continue;
                }
                renderer.NativePixelFootprint = 1;
                nativeTheme.Ensure(s, renderer, NativeCanvasSide);
                if (themeGeneration != nativeTheme.Generation)
                {
                    presenter.RefreshRoles(); themeGeneration = nativeTheme.Generation;
                    framePolicy.Invalidate(); previousRole = 0;
                    publicationPolicy.Invalidate();
                }
                var ci = new Native.CursorInfo { Size = Marshal.SizeOf<Native.CursorInfo>() };
                bool ok = Native.GetCursorInfo(ref ci);
                if (fixedNativeContext && Native.GetPhysicalCursorPos(out var physicalPosition)) ci.Position = physicalPosition;
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
                if (visible && !previousVisible) { simulation.NativeReappeared(); publicationPolicy.Invalidate(); }
                previousVisible = visible;
                bool pressed = supplied?.Pressed ?? (Native.GetAsyncKeyState(1) < 0 || Native.GetAsyncKeyState(2) < 0);
                var raw = new Vector2(ci.Position.X, ci.Position.Y);
                var simulated = simulation.Step(dt, new(raw, visible && inside, shape, pressed), s, nativeEffects: true);
                if (visible)
                {
                    var primary = displays.FirstOrDefault(d => d.Primary) ?? displays[0];
                    var target = displays.FirstOrDefault(d => d.Bounds.Contains(ci.Position)) ?? primary;
                    bool directRaster = CursorTargetRaster.Enabled && !target.Primary && target.CursorPixels == 32 && target.CursorPixels < primary.CursorPixels;
                    uint intendedDpi = directRaster ? (CursorTargetRaster.NativeCoordinates ? 1u : target.Dpi) : 0;
                    if (fixedNativeContext)
                    {
                        if (directRaster && monitorRasterContext == null) { monitorRasterContext = new CursorTargetRaster.DpiScope(1); presenter.InvalidateBitmaps(); }
                        else if (!directRaster && monitorRasterContext != null) { monitorRasterContext.Value.Dispose(); monitorRasterContext = null; presenter.InvalidateBitmaps(); }
                    }
                    float ratio = directRaster ? CursorTargetRaster.Scale(primary.CursorPixels, target.CursorPixels) : 1;
                    RasterDiagnostics = $"direct={directRaster}; primaryDpi={primary.Dpi}; targetDpi={target.Dpi}; geometry={ratio:F4}; creationDpi={intendedDpi}";
                    if (rasterSource != s || ratio != geometryRatio)
                    { rasterSource = s; geometryRatio = ratio; rasterGeometry = ratio == 1 ? s : CursorTargetRaster.Geometry(s, ratio); }
                    var renderSettings = rasterGeometry ?? s;
                    float footprint = directRaster ? 1 : CursorRasterQuality.ForMonitor(displays, ci.Position);
                    if (footprint != previousFootprint) { framePolicy.Invalidate(); previousFootprint = footprint; }
                    if (intendedDpi != previousRasterDpi) { framePolicy.Invalidate(); publicationPolicy.Invalidate(); previousRasterDpi = intendedDpi; }
                    renderer.NativePixelFootprint = footprint;
                    // The head stays on Windows' real hotspot continuously.
                    // The spring filters exposure and tilt, not the native image position.
                    var frame = NativeCursorPresenter.Anchored(simulated);
                    if (newMotion && trajectory != null && HistoryError == null)
                    {
                        double physicalNow = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                        frame = motionBuilder.Build(frame, trajectory.History, physicalNow, raw, s, dt);
                    }
                    if (!inside) frame = StaticFrame(s, shape, (float)now);
                    if (directRaster) frame = CursorTargetRaster.Frame(frame, s, renderSettings);
                    NativeGeometryRatio = directRaster ? ratio : 1;
                    var logicalFrame = frame;
                    frame = frameBudget.Constrain(frame, s, renderSettings, intendedDpi);
                    var bounds = NativeCursorPresenter.Bounds(frame, renderSettings);
                    bool limited = bounds.Width > 2048 || bounds.Height > 2048;
                    if (limited) { frame = CursorTargetRaster.Frame(StaticFrame(s, shape, (float)now), s, renderSettings); bounds = NativeCursorPresenter.Bounds(frame, renderSettings); }
                    if (role != previousRole) framePolicy.Invalidate();
                    if (framePolicy.NeedsRender(frame, renderSettings, bounds))
                    {
                        if (fixedNativeContext && directRaster)
                        {
                            if (Native.GetDpiForSystem() != 96) Native.SetThreadDpiAwarenessContext(-1);
                            Native.SetThreadCursorCreationScaling(1);
                        }
                        double start = clock.Elapsed.TotalMilliseconds;
                        bool allRoles = !newMotion || publicationPolicy.AllRoles(logicalFrame, renderSettings);
                        NativeFrameBudget.Prepared prepared;
                        try { prepared = frameBudget.Prepare(renderer, presenter, frame, renderSettings, motionBuilder, s.CaptureCompatibility, transportSide: NativeCanvasSide); }
                        catch { NativeBudgetMisses++; throw; }
                        presenter.PresentSprite(prepared.Sprite, role, fixedNativeContext ? 0 : intendedDpi, allRoles);
                        NativeScaleRatio = frameBudget.ScaleRatio; FittedFrames = frameBudget.AdjustedFrames;
                        NativeMaximumScale = frameBudget.MaximumScale;
                        ExposurePixels = prepared.Frame.Exposure?.Length ?? 0; ExposureMilliseconds = prepared.Frame.Exposure?.DurationMs ?? 0;
                        CursorCreateMs = presenter.LastCreateMs; CursorSetMs = presenter.LastSetMs;
                        if (allRoles) FamilyFrames++; else ActiveRoleFrames++;
                        PeakCursorWidth = Math.Max(PeakCursorWidth, prepared.Sprite.Width);
                        PeakCursorHeight = Math.Max(PeakCursorHeight, prepared.Sprite.Height);
                        RenderMilliseconds = RenderMilliseconds * .9 + (clock.Elapsed.TotalMilliseconds - start) * .1;
                        RenderedFrames++; fpsFrames++;
                    }
                    previousRole = role;
                    Presentation = limited ? "Windows system cursor (extreme motion bounds limited)" : (s.CaptureCompatibility || CompactCursorRequired)
                        ? $"GPU system cursor; compact capture frames <={NativeCanvasSide}px" : "GPU frames in Windows system cursor";
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
            monitorRasterContext?.Dispose();
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
