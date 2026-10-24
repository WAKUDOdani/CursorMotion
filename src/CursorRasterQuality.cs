namespace CursorMotion;

// Prefilter vector edges for the destination cursor pixel grid. Do not resize
// images, alter cursor creation DPI, or feed monitor scaling into mouse physics.
internal static class CursorRasterQuality
{
    internal static float Footprint(int primaryCursorPixels, int targetCursorPixels) =>
        primaryCursorPixels > 0 && targetCursorPixels > 0
            ? Math.Clamp(primaryCursorPixels / (float)targetCursorPixels, 1f, 4f) : 1f;

    internal static float ForMonitor(IReadOnlyList<Native.Display> displays, Native.Point position)
    {
        var primary = displays.FirstOrDefault(d => d.Primary) ?? displays.First();
        var target = displays.FirstOrDefault(d => d.Bounds.Contains(position)) ?? primary;
        if (target == primary) return 1;
        return Footprint(Native.GetSystemMetricsForDpi(13, primary.Dpi), Native.GetSystemMetricsForDpi(13, target.Dpi));
    }
}
