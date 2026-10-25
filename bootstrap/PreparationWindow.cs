using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CursorMotion.Bootstrap;

internal sealed record PreparationState(string Status, double? Progress);

// A short-lived native UI thread keeps the indicator responsive during hashing
// and large-file extraction. It never activates a window or changes the cursor.
internal sealed class PreparationWindow : IDisposable
{
    readonly ManualResetEventSlim stop = new();
    readonly Thread thread;
    readonly bool visible;
    bool disposed;
    volatile PreparationState state = new("Проверяем файл приложения…", null);
    internal Exception? Failure { get; private set; }
    internal volatile bool Initialized;
    internal long WindowStyle { get; private set; }
    internal bool WasVisible { get; private set; }
    internal bool ThreadEnded => !thread.IsAlive;
    internal PreparationWindow(bool visible = true)
    {
        this.visible = visible;
        thread = new(Run) { IsBackground = true, Name = "CursorMotion preparation" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
    }
    internal void Report(string status, double? progress = null) => state = new(status, progress.HasValue ? Math.Clamp(progress.Value, 0, 1) : null);
    void Run()
    {
        nint window = 0;
        try
        {
            Native.GetCursorPos(out var point);
            nint monitor = Native.MonitorFromPoint(point, 2);
            var info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
            if (!Native.GetMonitorInfo(monitor, ref info)) throw new InvalidOperationException("Cannot locate preparation monitor");
            window = Native.CreateWindowEx(0x08080080, Native.PreparationClass(), "CursorMotion — Подготовка к запуску", 0x80000000,
                info.Work.Left, info.Work.Top, 1, 1, 0, 0, 0, 0);
            if (window == 0) throw new InvalidOperationException("Cannot create preparation window");
            float scale = Math.Max(1, Native.GetDpiForWindow(window) / 96f);
            using var visual = new PreparationVisual(scale);
            var destination = new Native.Point
            {
                X = info.Work.Left + (info.Work.Right - info.Work.Left - visual.Width) / 2,
                Y = info.Work.Top + (info.Work.Bottom - info.Work.Top - visual.Height) / 2
            };
            Native.SystemParametersInfo(0x1042, 0, out int animations, 0);
            var clock = Stopwatch.StartNew(); bool shown = false;
            while (!stop.IsSet)
            {
                var current = state;
                visual.Draw(current, animations != 0 ? clock.Elapsed.TotalSeconds : 0, animations == 0);
                visual.Present(window, destination);
                if (!shown)
                {
                    if (visible) Native.ShowWindow(window, 4); shown = true;
                    WindowStyle = Native.GetWindowLongPtr(window, -20).ToInt64(); WasVisible = Native.IsWindowVisible(window); Initialized = true;
                }
                while (Native.PeekMessage(out var message, 0, 0, 0, 1)) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); }
                stop.Wait(33);
            }
        }
        catch (Exception ex) { Failure = ex; } // Decoration must never prevent startup.
        finally { if (window != 0) Native.DestroyWindow(window); }
    }
    public void Dispose() { if (disposed) return; disposed = true; stop.Set(); if (thread.Join(2000)) stop.Dispose(); }
}

internal static partial class Native
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);
    static readonly WindowProcedure preparationProcedure = PreparationMessage;
    static readonly object preparationClassGate = new();
    static ushort preparationClass;
    const string preparationClassName = "CursorMotion.Preparation";
    internal static string PreparationClass()
    {
        lock (preparationClassGate)
        {
            if (preparationClass == 0)
            {
                var value = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = GetModuleHandle(null),
                    Procedure = Marshal.GetFunctionPointerForDelegate(preparationProcedure), Name = preparationClassName };
                preparationClass = RegisterClassEx(ref value);
                if (preparationClass == 0) throw new InvalidOperationException("Cannot register preparation window");
            }
        }
        return preparationClassName;
    }
    static nint PreparationMessage(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == 0x21) return 3; // MA_NOACTIVATE
        if (message == 0x14) return 1; // The layered renderer owns the background.
        if (message == 0xF) { ValidateRect(window, 0); return 0; }
        return DefWindowProc(window, message, wParam, lParam);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct WindowClass
    {
        internal uint Size, Style; internal nint Procedure; internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background, Menu; internal string Name; internal nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal uint Size; internal Rect Bounds, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] internal struct Blend { internal byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo
    {
        internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
        internal uint Compression, SizeImage; internal int XPixels, YPixels; internal uint Used, Important;
    }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint DefWindowProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] static extern bool ValidateRect(nint window, nint rectangle);
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool SystemParametersInfo(uint action, uint param, out int value, uint flags);
    [DllImport("user32.dll")] internal static extern bool UpdateLayeredWindow(nint window, nint screen, ref Point destination, ref Size size, nint source, ref Point origin, uint key, ref Blend blend, uint flags);
    [DllImport("gdi32.dll")] internal static extern nint CreateCompatibleDC(nint source);
    [DllImport("gdi32.dll")] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
}
