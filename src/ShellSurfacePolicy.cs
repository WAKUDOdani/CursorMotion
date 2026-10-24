using System.Diagnostics;
using System.Text;

namespace CursorMotion;

// Unprivileged portable windows cannot cover every Windows shell surface.
// Use our native custom cursor on those surfaces; never raise privileges or consume input.
internal sealed class ShellSurfacePolicy
{
    readonly Dictionary<nint, (long Time, uint Pid, bool Shell)> cache = new();
    public static bool IsShellSurface(string className, string processName)
    {
        if (processName.Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("ShellHost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase)
            || processName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase)) return true;
        return className is "TaskListThumbnailWnd" or "TaskListOverlayWnd" or "Shell_TrayWnd"
            or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "ControlCenterWindow"
            or "TopLevelWindowForOverflowXamlIsland" or "Shell_InputSwitchTopLevelWindow";
    }
    bool IsShell(nint window, long now)
    {
        Native.GetWindowThreadProcessId(window, out uint pid);
        if (cache.TryGetValue(window, out var entry) && entry.Pid == pid && now - entry.Time < 1000) return entry.Shell;
        var name = new StringBuilder(256); Native.GetClassName(window, name, name.Capacity);
        string process = "";
        try { using var owner = Process.GetProcessById((int)pid); process = owner.ProcessName; } catch (ArgumentException) { } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        bool shell = IsShellSurface(name.ToString(), process);
        if (cache.Count > 128) cache.Clear();
        cache[window] = (now, pid, shell); return shell;
    }
    public bool UseNativePointer(Native.Point point, nint overlay, long now)
    {
        nint hit = Native.WindowFromPoint(point);
        nint root = hit != 0 ? Native.GetAncestor(hit, 2) : 0;
        if (root != 0 && root != overlay && IsShell(root, now)) return true;
        // A window which remains above us after a topmost raise can belong to a
        // shell/system band. Use the OS cursor instead of leaving ours occluded.
        nint above = Native.GetWindow(overlay, 3);
        for (int count = 0; above != 0 && count < 32; count++, above = Native.GetWindow(above, 3))
        {
            if (!Native.IsWindowVisible(above) || Native.IsIconic(above) || !Native.GetWindowRect(above, out var bounds) || !bounds.Contains(point)) continue;
            if (Native.DwmGetWindowAttribute(above, 14, out uint cloaked, 4) == 0 && cloaked != 0) continue;
            return true;
        }
        return false;
    }
}
