using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace CursorMotion;

// Separate process: also restores the cursor if the renderer crashes or stops responding.
internal sealed class CursorGuard : IDisposable
{
    readonly MemoryMappedFile map;
    readonly MemoryMappedViewAccessor state;
    readonly EventWaitHandle stop;
    readonly Process child;
    bool hidden;
    public string StateName { get; }
    public int WatchdogId => child.Id;
    public CursorGuard()
    {
        string token = Guid.NewGuid().ToString("N");
        StateName = "Local\\CursorMotion.State." + token;
        map = MemoryMappedFile.CreateNew(StateName, 32);
        state = map.CreateViewAccessor();
        stop = new(false, EventResetMode.ManualReset, "Local\\CursorMotion.Stop." + token);
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\CursorMotion.Ready." + token);
        if (!Native.MagInitialize()) throw Native.Error("MagInitialize");
        try
        {
            child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { "--watchdog", Environment.ProcessId.ToString(), token }, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("Не удалось запустить защиту курсора.");
            if (!ready.WaitOne(5000) || child.HasExited) throw new InvalidOperationException("Защита курсора не готова. Эффект не включён.");
        }
        catch { Native.MagUninitialize(); stop.Set(); state.Dispose(); map.Dispose(); stop.Dispose(); throw; }
    }
    public void Heartbeat()
    {
        if (child.HasExited) throw new InvalidOperationException("Защита курсора завершилась. Эффект отключён.");
        state.Write(0, Environment.TickCount64);
    }
    public void SetHidden(bool value)
    {
        if (value == hidden) return;
        Heartbeat();
        // Arm recovery before the API call; disarm only after successful restoration.
        if (value) state.Write(8, 1);
        if (!Native.MagShowSystemCursor(!value)) throw Native.Error("MagShowSystemCursor");
        hidden = value;
        if (!value) state.Write(8, 0);
    }
    public void ArmThemeRecovery() { Heartbeat(); state.Write(20, 1); }
    public void RestoreTheme()
    {
        if (state.ReadInt32(20) == 0) return;
        // Reload the user's registered scheme. Never write cursor paths to the registry.
        if (!Native.SystemParametersInfo(0x0057, 0, 0, 0)) throw Native.Error("Restore system cursor scheme");
        state.Write(20, 0);
    }
    public void Dispose()
    {
        Native.MagShowSystemCursor(true); hidden = false; state.Write(8, 0);
        try { RestoreTheme(); } catch (Exception ex) { Program.Log(ex); }
        stop.Set();
        child.WaitForExit(2000); child.Dispose(); state.Dispose(); map.Dispose(); stop.Dispose(); Native.MagUninitialize();
    }
    public static int Watch(int parentId, string token)
    {
        bool initialized = Native.MagInitialize();
        try
        {
            using var parent = Process.GetProcessById(parentId);
            using var map = MemoryMappedFile.OpenExisting("Local\\CursorMotion.State." + token);
            using var state = map.CreateViewAccessor();
            using var stop = EventWaitHandle.OpenExisting("Local\\CursorMotion.Stop." + token);
            using var ready = EventWaitHandle.OpenExisting("Local\\CursorMotion.Ready." + token);
            if (!initialized) return 2;
            ready.Set();
            int reason;
            while (true)
            {
                if (stop.WaitOne(100)) { reason = 3; break; }
                if (parent.HasExited) { reason = 2; break; }
                if ((state.ReadInt32(8) != 0 || state.ReadInt32(20) != 0) && Environment.TickCount64 - state.ReadInt64(0) > 2000) { reason = 1; break; }
            }
            if (state.ReadInt32(8) != 0) state.Write(12, Native.MagShowSystemCursor(true) ? 1 : -1);
            if (state.ReadInt32(20) != 0) state.Write(24, Native.SystemParametersInfo(0x0057, 0, 0, 0) ? 1 : -1);
            state.Write(16, reason);
            return 0;
        }
        catch { if (initialized) Native.MagShowSystemCursor(true); Native.SystemParametersInfo(0x0057, 0, 0, 0); return 1; }
        finally { if (initialized) Native.MagUninitialize(); }
    }
    public static void Restore()
    {
        if (Native.MagInitialize()) { Native.MagShowSystemCursor(true); Native.MagUninitialize(); }
        Native.SystemParametersInfo(0x0057, 0, 0, 0);
    }
}
