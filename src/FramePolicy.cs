using System.Numerics;

namespace CursorMotion;

internal sealed class FramePolicy
{
    RenderFrame previous;
    Native.Rect previousBounds;
    CursorAppearance? previousA, previousB;
    double previousFalloff;
    double previousDensity;
    bool valid;
    bool previousCaptureCompatibility;
    public void Invalidate() => valid = false;
    public bool NeedsRender(RenderFrame frame, Settings settings, Native.Rect bounds)
    {
        bool animated = frame.ShapeB == 2 || (frame.Morph < .999f && frame.ShapeA == 2);
        var appearanceA = settings.AppearanceFor((int)frame.ShapeA);
        var appearanceB = settings.AppearanceFor((int)frame.ShapeB);
        var key = frame with { Animation = animated ? frame.Animation : 0 };
        if (valid && previousCaptureCompatibility == settings.CaptureCompatibility && previousBounds.Equals(bounds) && previousA == appearanceA && previousB == appearanceB && previousFalloff == settings.TrailFalloff && previousDensity == settings.BlurDensity
            && Close(previous.Start, key.Start) && Close(previous.Control, key.Control) && Close(previous.End, key.End)
            && Math.Abs(previous.Scale - key.Scale) < .00001f && Math.Abs(previous.Tilt - key.Tilt) < .000001f
            && previous.ShapeA == key.ShapeA && previous.ShapeB == key.ShapeB && previous.Morph == key.Morph
            && previous.Animation == key.Animation && previous.Opacity == key.Opacity && previous.Samples == key.Samples && previous.LockHead == key.LockHead
            && (previous.Exposure == null ? key.Exposure == null : previous.Exposure.Same(key.Exposure))) return false;
        previous = key; previousBounds = bounds; previousA = appearanceA; previousB = appearanceB; previousFalloff = settings.TrailFalloff; valid = true;
        previousCaptureCompatibility = settings.CaptureCompatibility;
        previousDensity = settings.BlurDensity;
        return true;
    }
    static bool Close(Vector2 first, Vector2 second) => Vector2.DistanceSquared(first, second) < .000001f;
}
