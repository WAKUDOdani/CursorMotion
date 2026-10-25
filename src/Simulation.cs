using System.Numerics;

namespace CursorMotion;

internal readonly record struct CursorInput(Vector2 Position, bool Visible, int Shape, bool Pressed);
internal readonly record struct RenderFrame(Vector2 Start, Vector2 Control, Vector2 End, float Scale, float Tilt, float ShapeA, float ShapeB, float Morph, float Animation, float Opacity, int Samples, bool LockHead = false, ExposurePath? Exposure = null);

internal sealed class Simulation
{
    const int Capacity = 2048;
    readonly double[] times = new double[Capacity];
    readonly Vector2[] positions = new Vector2[Capacity];
    readonly Vector2[] rawPositions = new Vector2[Capacity];
    int index = -1, count;
    Vector2 previous, position, velocity;
    double time, lastMove, animation;
    float opacity, tilt, click = 1, clickVelocity, morph = 1;
    int shapeA, shapeB;
    bool resetRequested = true;
    bool settled;
    CursorInput lastInput;
    Settings? lastSettings;
    bool lastNativeEffects;
    RenderFrame lastFrame;
    public long ReusedSteps { get; private set; }
    public bool MotionLimited { get; private set; }
    internal static float MaximumVisualLag(float scale, Settings settings, int shapeA = 0, int shapeB = 0)
    {
        float margin = GpuRenderer.AppearanceMargin(scale, settings.AppearanceFor(shapeA), settings.AppearanceFor(shapeB));
        float exposureBudget = settings.Shutter > 0 ? MaximumSampleSpacing(scale) * settings.MaxSamples : 0;
        // Reserve the full glyph/shadow and exposure extent plus 64px rounding
        // headroom inside the 2048px native frame. This is an emergency frame
        // bound, not a small style-dependent clamp on the smoothing control.
        return Math.Clamp(1984 - 2 * margin - exposureBudget, 128, 1024);
    }
    internal static float MaximumSampleSpacing(float scale) => Math.Max(.5f, scale * .75f);
    public void Reset() => resetRequested = true;
    // Native Windows visibility handles typing. Reappearance must not add a fade-in delay.
    public void NativeReappeared() { resetRequested = true; opacity = 1; }
    void Push(Vector2 raw) { index = (index + 1) % Capacity; times[index] = time; positions[index] = position; rawPositions[index] = raw; count = Math.Min(count + 1, Capacity); }
    Vector2 At(double t, bool actual = false)
    {
        var history = actual ? rawPositions : positions;
        int i = index;
        if (t >= times[i]) return history[i];
        for (int n = 1; n < count; n++)
        {
            int j = (i + Capacity - 1) % Capacity;
            if (t >= times[j]) return Vector2.Lerp(history[j], history[i], (float)((t - times[j]) / Math.Max(1e-12, times[i] - times[j])));
            i = j;
        }
        return history[i];
    }
    public RenderFrame Step(double seconds, CursorInput input, Settings s, bool nativeEffects = false)
    {
        double dt = Math.Clamp(seconds, .000001, .1);
        MotionLimited = false;
        int shape = s.Style >= 0 ? s.Style : Math.Clamp(input.Shape, 0, 7);
        var appearance = s.AppearanceFor(shape);
        Vector2 raw = input.Position;
        if (raw != previous || input.Pressed) lastMove = time;
        bool want = input.Visible && (appearance.IdleHide <= 0 || time - lastMove < appearance.IdleHide);
        if (!resetRequested && seconds <= .1 && settled && input == lastInput && ReferenceEquals(s, lastSettings) && nativeEffects == lastNativeEffects && want == (opacity == 1))
        {
            time += dt; animation += dt;
            ReusedSteps++;
            return lastFrame with { Animation = (float)animation };
        }
        // Idle reuse advances the clock without adding samples. On resuming,
        // seed history at the current stationary instant; otherwise interpolation
        // spans that entire idle gap and invents motion before the mouse moved.
        if (settled && !resetRequested) { count = 0; Push(previous); }
        if (resetRequested || seconds > .1 || Vector2.DistanceSquared(raw, previous) > 16384 * 16384 || (want && opacity <= .001))
        {
            position = previous = raw; velocity = Vector2.Zero; tilt = 0; count = 0;
            click = 1; clickVelocity = 0; resetRequested = false; Push(raw);
        }
        opacity = Math.Clamp(opacity + (float)(want ? dt / .12 : -dt / .12), 0, 1);
        if (input.Visible && shape != shapeB)
        {
            if (shape == shapeA && morph < 1)
            {
                // Reverse from the current blended image. Restarting at either
                // endpoint flashed arrow/text when an app alternated WM_SETCURSOR.
                (shapeA, shapeB) = (shapeB, shapeA);
                morph = 1 - morph;
            }
            else
            {
                if (morph >= .5) shapeA = shapeB;
                shapeB = shape; morph = 0;
            }
        }
        morph = appearance.MorphTime > 0 ? Math.Min(1, morph + (float)(dt / (appearance.MorphTime / 1000))) : 1;
        float blend = morph * morph * (3 - 2 * morph);
        double sizeA = s.AppearanceFor(shapeA).Scale, sizeB = s.AppearanceFor(shapeB).Scale;
        float baseScale = (float)(sizeA + (sizeB - sizeA) * blend);
        float omega = s.Smoothing > 0 ? (float)(4 / (Math.Max(s.Smoothing, 10) / 1000)) : 0;
        // Smaller steps at short response times keep every exposed damping value stable.
        double maxStep = omega > 0 ? Math.Min(.002, .35 / omega) : .002;
        int steps = Math.Max(1, (int)Math.Ceiling(dt / maxStep));
        float h = (float)(dt / steps);
        for (int i = 1; i <= steps; i++)
        {
            Vector2 target = Vector2.Lerp(previous, raw, (float)i / steps);
            if (omega == 0) { velocity = (target - position) / h; position = target; }
            else { velocity += (omega * omega * (target - position) - 2 * (float)s.Damping * omega * velocity) * h; position += velocity * h; }
            // Critical damping: a click is a smooth press/release, without
            // undershoot or a size rebound that looks like a flash.
            clickVelocity += (1600 * ((input.Pressed ? (float)s.ClickScale : 1) - click) - 80 * clickVelocity) * h;
            click += clickVelocity * h;
            // Bound only the drawn spring, never Windows' mouse/click position.
            // Large hotspot offsets made enormous native bitmaps and repeatedly
            // crossed the presenter's hard fallback during fast 4K reversals.
            var offset = position - target;
            float limit = MaximumVisualLag(baseScale * click, s, shapeA, shapeB), distance = offset.Length();
            if (omega > 0 && distance > limit)
            {
                var normal = offset / distance;
                position = target + normal * limit;
                // Do not inject the raw mouse's high velocity into the spring:
                // that would create a fresh overshoot when the mouse stops.
                float outward = Vector2.Dot(velocity, normal);
                if (outward > 0) velocity -= outward * normal;
                MotionLimited = true;
            }
            // Absolute desktop coordinates eventually round away tiny spring
            // increments. Finish a stationary subpixel settle exactly, so the
            // sharp frame and static cache are reached even on wide desktops.
            if (raw == previous && Vector2.DistanceSquared(position, raw) < .0025f && velocity.LengthSquared() < 1)
            { position = raw; velocity = Vector2.Zero; }
            time += h; Push(target);
        }
        previous = raw;
        float targetTilt = Math.Clamp(velocity.X / 1000 * (float)(s.Tilt * Math.PI / 180), -(float)Math.PI / 6, (float)Math.PI / 6);
        tilt += (targetTilt - tilt) * (float)(1 - Math.Exp(-dt * 14));
        animation += dt;
        double shutter = s.Shutter / 360 / s.ShutterFps;
        // The exact native head must not erase the motion signal. Integrate the
        // real path; smoothing gives that path more temporal persistence while
        // the existing spring still filters tilt. Shutter=0 remains exactly off.
        if (nativeEffects) shutter *= 1 + s.Smoothing / 100;
        Vector2 exposureEnd = nativeEffects ? raw : position;
        (Vector2 Start, Vector2 Control, float Length) Exposure(double duration)
        {
            var a = At(time - duration, nativeEffects); var m = At(time - duration * .5, nativeEffects);
            var c = 2 * m - .5f * (a + exposureEnd);
            // A three-point quadratic fit can put its control beyond every
            // recorded point after stopping/reversing, sending blur samples
            // past the stationary hotspot. Keep the fit in the sampled envelope.
            c = Vector2.Clamp(c, Vector2.Min(a, Vector2.Min(m, exposureEnd)), Vector2.Max(a, Vector2.Max(m, exposureEnd)));
            // Quadratic derivative is 2*lerp(c-a, end-c, t): this bounds
            // every adjacent midpoint sample, including sharp curved turns.
            return (a, c, 2 * Math.Max(Vector2.Distance(a, c), Vector2.Distance(c, exposureEnd)));
        }
        var exposure = Exposure(shutter);
        float spacing = MaximumSampleSpacing(baseScale * click), budget = spacing * s.MaxSamples;
        if (exposure.Length > budget)
        {
            // Truncate time exposure continuously, rather than drawing isolated
            // cursor copies or scaling a long curve into an unrelated shape.
            double low = 0, high = shutter;
            for (int i = 0; i < 12; i++)
            {
                double duration = (low + high) * .5;
                if (Exposure(duration).Length <= budget) low = duration; else high = duration;
            }
            exposure = Exposure(low); MotionLimited = true;
        }
        Vector2 start = exposure.Start, control = exposure.Control;
        int samples = exposure.Length >= .5 && shutter > 0 ? Math.Clamp((int)Math.Ceiling(exposure.Length / spacing), 2, s.MaxSamples) : 1;
        settled = Vector2.DistanceSquared(position, raw) < .0004f && velocity.LengthSquared() < 1
            && Math.Abs(click - (input.Pressed ? (float)s.ClickScale : 1)) < .00001f && Math.Abs(clickVelocity) < .001f
            && Math.Abs(tilt) < .0001f && samples == 1 && morph == 1 && (opacity == 0 || opacity == 1);
        lastInput = input; lastSettings = s; lastNativeEffects = nativeEffects;
        lastFrame = new(start, control, exposureEnd, (float)(sizeA + (sizeB - sizeA) * blend) * click, tilt, shapeA, shapeB, blend, (float)animation, opacity, samples);
        return lastFrame;
    }
}
