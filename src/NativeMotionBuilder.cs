using System.Numerics;

namespace CursorMotion;

internal sealed class NativeMotionBuilder
{
    float exposureFraction = 1;
    internal RenderFrame Build(RenderFrame pose, MotionHistory history, double now, Vector2 current, Settings settings, double dt)
    {
        exposureFraction += (1 - exposureFraction) * (float)(1 - Math.Exp(-Math.Max(0, dt) * 5));
        double duration = settings.Shutter / 360 / settings.ShutterFps * (1 + settings.Smoothing / 100);
        var path = history.Exposure(now, current, duration * exposureFraction);
        if (path is { } large)
        {
            var bounds = large.Bounds();
            float extent = Math.Max(bounds.Max.X - bounds.Min.X, bounds.Max.Y - bounds.Min.Y);
            if (extent > 320) path = large.Recent(256 / extent);
        }
        return Attach(pose, path, settings.MaxSamples);
    }
    internal RenderFrame FitAgain(RenderFrame frame, int requiredWidth, int requiredHeight, int limit, int maxSamples)
    {
        if (frame.Exposure == null) return frame;
        float fraction = Math.Clamp(.8f * limit / Math.Max(requiredWidth, requiredHeight), .05f, .85f);
        exposureFraction = Math.Clamp(exposureFraction * fraction, .01f, 1);
        return Attach(frame, frame.Exposure.Recent(fraction), maxSamples);
    }
    static RenderFrame Attach(RenderFrame pose, ExposurePath? path, int maxSamples) => pose with
    {
        Start = path?.Points[0] ?? Vector2.Zero,
        Control = path?.At(.5f) ?? Vector2.Zero, End = Vector2.Zero,
        Exposure = path, Samples = path == null ? 1 : Math.Clamp((int)Math.Ceiling(path.Length / .85f), 4, Math.Max(4, maxSamples)), LockHead = true
    };
}
