using System.Runtime.InteropServices;

namespace CursorMotion;

// Our GPU frames already contain physical pixels, a real hotspot and the entire
// exposure. Windows must not downscale/re-crop those frames on another monitor.
// Only cursor creation on this thread is affected; the user's DPI stays intact.
internal readonly struct CursorPixelScaling : IDisposable
{
    internal const uint None = 1, Default = 2; // Windows SDK winuser.h
    readonly uint previous;
    public static bool Available => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SetThreadCursorCreationScaling(uint cursorDpi);
    CursorPixelScaling(uint previous) => this.previous = previous;
    public static CursorPixelScaling Enter()
    {
        if (!Available) return default;
        uint previous = SetThreadCursorCreationScaling(None);
        if (previous == 0) throw Native.Error("Configure physical-pixel cursor creation");
        return new(previous);
    }
    public void Dispose()
    { if (previous != 0) SetThreadCursorCreationScaling(previous); }
}
