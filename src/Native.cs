using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CursorMotion;

internal static class Native
{
    // DirectComposition owns per-pixel alpha. A legacy layered redirection
    // surface behind it would make transparent pixels appear black.
    internal const uint NoRedirectionBitmap = 0x00200000;
    internal const uint LayeredWindow = 0x00080000;
    internal const uint CompositionWindowStyle = NoRedirectionBitmap | 0x20 | 0x80 | 0x08000000 | 0x8;
    // LAYERED + TRANSPARENT provides cross-process click-through for the desktop cursor.
    internal const uint CursorWindowStyle = LayeredWindow | 0x20 | 0x80 | 0x08000000 | 0x8;
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Size { public int Width, Height; public Size(int w, int h) { Width = w; Height = h; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; public bool Contains(Point p) => p.X >= Left && p.Y >= Top && p.X < Right && p.Y < Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct CursorInfo { public int Size, Flags; public nint Cursor; public Point Position; }
    [StructLayout(LayoutKind.Sequential)] internal struct IconInfo { public bool Icon; public uint HotX, HotY; public nint Mask, Color; }
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetIconInfo(nint icon, out IconInfo info);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct IconInfoEx
    {
        public uint Size; public int Icon; public uint HotX, HotY; public nint Mask, Color; public ushort Resource;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetIconInfoEx(nint icon, ref IconInfoEx info);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern int SetBitmapBits(nint bitmap, uint count, byte[] bits);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool DrawIconEx(nint dc, int x, int y, nint icon, int width, int height, uint step, nint brush, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint Used, Important; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] internal struct Msg { public nint Window; public uint Message; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass { public uint Size, Style; public WndProc Proc; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? MenuName; public string ClassName; public nint SmallIcon; }
    internal delegate nint WndProc(nint hwnd, uint message, nuint wparam, nint lparam);
    internal delegate void WinEventProc(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    internal delegate bool MonitorProc(nint monitor, nint dc, ref Rect rect, nint data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint LoadCursor(nint instance, nint id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetSystemCursor(nint cursor, uint role);
    [DllImport("user32.dll")] internal static extern bool DestroyCursor(nint cursor);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint CopyImage(nint image, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool SystemParametersInfo(uint action, uint parameter, nint value, uint flags);
    [DllImport("user32.dll")] internal static extern bool EnumDisplayMonitors(nint dc, nint rect, MonitorProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern bool UnregisterClass(string className, nint instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wparam, nint lparam);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] internal static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect bounds);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, System.Text.StringBuilder name, int capacity);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out uint value, uint size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint SendMessage(nint hwnd, uint message, nuint parameter, nint data);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(nint hwnd, nint dc, ref Point dest, ref Size size, nint sourceDc, ref Point source, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Msg msg, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Msg msg);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] internal static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("Magnification.dll", SetLastError = true)] internal static extern bool MagInitialize();
    [DllImport("Magnification.dll")] internal static extern bool MagUninitialize();
    [DllImport("Magnification.dll", SetLastError = true)] internal static extern bool MagShowSystemCursor(bool show);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool CloseDesktop(nint desktop);
    [DllImport("user32.dll")] internal static extern nint GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWaitableTimerEx(nint attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetWaitableTimer(nint timer, ref long dueTime, int period, nint completion, nint argument, bool resume);
    [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(nint handle, uint milliseconds);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool GetUserObjectInformation(nint obj, int index, System.Text.StringBuilder name, uint size, out uint needed);
    internal static bool OnInputDesktop()
    {
        nint desktop = OpenInputDesktop(0, false, 1);
        if (desktop == 0) return false;
        try
        {
            var a = new System.Text.StringBuilder(256); var b = new System.Text.StringBuilder(256);
            return GetUserObjectInformation(desktop, 2, a, 512, out _) && GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, b, 512, out _) && a.ToString() == b.ToString();
        }
        finally { CloseDesktop(desktop); }
    }
    internal static Exception Error(string action) => new Win32Exception(Marshal.GetLastWin32Error(), action);
    internal sealed record Display(Rect Bounds, bool Primary)
    {
        public override string ToString() => $"{Bounds.Width} × {Bounds.Height} · ({Bounds.Left}, {Bounds.Top}){(Primary ? " · основной" : "")}";
    }
    internal static List<Display> Displays()
    {
        var result = new List<Display>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Rect rect, nint data) =>
        {
            MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info)) result.Add(new(info.Monitor, (info.Flags & 1) != 0));
            return true;
        }, 0);
        if (result.Count == 0) throw new InvalidOperationException("Не удалось получить список мониторов.");
        return result;
    }
    internal static Rect Region(Settings s, List<Display> displays)
    {
        if (s.Monitor == -1) return new() { Left = s.CustomX, Top = s.CustomY, Right = s.CustomX + s.CustomWidth, Bottom = s.CustomY + s.CustomHeight };
        if (s.Monitor == -3) return new() { Left = displays.Min(d => d.Bounds.Left), Top = displays.Min(d => d.Bounds.Top), Right = displays.Max(d => d.Bounds.Right), Bottom = displays.Max(d => d.Bounds.Bottom) };
        return s.Monitor >= 0 && s.Monitor < displays.Count ? displays[s.Monitor].Bounds : displays.FirstOrDefault(d => d.Primary, displays[0]).Bounds;
    }
}
