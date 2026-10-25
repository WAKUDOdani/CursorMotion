using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace CursorMotion;

// Passive OS history reader. No hook, input interception, cursor warp or mouse
// preference changes. The renderer always uses the latest physical hotspot.
internal sealed class NativeTrajectorySampler : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct MousePoint { public int X, Y; public uint Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] static extern int GetMouseMovePointsEx(uint size, ref MousePoint point, [Out] MousePoint[] points, int count, uint resolution);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("api-ms-win-core-realtime-l1-1-1.dll")] static extern void QueryInterruptTimePrecise(out ulong time);
    internal static void CheckNativeClock()
    {
        ulong coarse = GetTickCount64(); QueryInterruptTimePrecise(out ulong precise);
        if (Math.Abs(precise / 10000d - coarse) > 100) throw new InvalidOperationException("Native history clock epochs differ");
    }
    readonly ManualResetEventSlim stop = new();
    readonly Thread thread;
    internal readonly MotionHistory History = new();
    internal volatile bool Enabled;
    internal long Samples { get; private set; }
    internal Exception? Failure { get; private set; }
    internal NativeTrajectorySampler()
    {
        thread = new(Run) { IsBackground = true, Name = "CursorMotion physical history", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }
    void Run()
    {
        nint previous = Native.SetThreadDpiAwarenessContext(-4), timer = 0;
        try
        {
            timer = Native.CreateWaitableTimerEx(0, null, 2, 0x1F0003);
            if (timer == 0) throw Native.Error("Create history sampling timer");
            var points = new MousePoint[64]; Native.Point last = new(int.MinValue, int.MinValue);
            long movedAt = 0;
            while (!stop.IsSet)
            {
                if (!Enabled) { stop.Wait(20); continue; }
                if (Native.GetPhysicalCursorPos(out var physical))
                {
                    long nowTicks = Stopwatch.GetTimestamp(), nowMs = Environment.TickCount64;
                    double now = nowTicks / (double)Stopwatch.Frequency;
                    if (physical.X != last.X || physical.Y != last.Y)
                    {
                        movedAt = nowMs; last = physical;
                        // OS mouse messages use boot time, while modern .NET's
                        // TickCount can exclude sleep. Match the native clock and
                        // retain a high-resolution physical sample even when OS
                        // event timestamps collapse to the same clock tick.
                        ulong bootMs = GetTickCount64();
                        QueryInterruptTimePrecise(out ulong preciseBoot);
                        double epoch = now - preciseBoot / 10000000d;
                        var query = new MousePoint { X = physical.X & 0xFFFF, Y = physical.Y & 0xFFFF };
                        int found = GetMouseMovePointsEx((uint)Marshal.SizeOf<MousePoint>(), ref query, points, points.Length, 1);
                        for (int i = found - 1; i >= 0; i--)
                        {
                            uint age = unchecked((uint)bootMs - points[i].Time);
                            if (age > 1000) continue;
                            int x = points[i].X > 32767 ? points[i].X - 65536 : points[i].X;
                            int y = points[i].Y > 32767 ? points[i].Y - 65536 : points[i].Y;
                            double eventTime = epoch + (bootMs - age) / 1000d;
                            if (eventTime <= now && History.Add(eventTime, new(x, y))) Samples++;
                        }
                        if (History.Add(now, new(physical.X, physical.Y))) Samples++;
                    }
                }
                long due = -(Environment.TickCount64 - movedAt < 100 ? 20000L : 80000L);
                if (!Native.SetWaitableTimer(timer, ref due, 0, 0, 0, false)) throw Native.Error("Arm history sampling timer");
                Native.WaitForSingleObject(timer, 20);
            }
        }
        catch (Exception ex) { Failure = ex; }
        finally { if (timer != 0) Native.CloseHandle(timer); if (previous != 0) Native.SetThreadDpiAwarenessContext(previous); }
    }
    public void Dispose() { stop.Set(); if (thread.Join(1000)) stop.Dispose(); }
}
