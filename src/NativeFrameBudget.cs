using System.Numerics;

namespace CursorMotion;

// Fits the actual native transport, not the user's saved size. The glyph is
// rerendered from its SDF; no bitmap resize or position filtering is introduced.
internal sealed class NativeFrameBudget
{
    Settings? source;
    uint context;
    float maximumScale = float.PositiveInfinity;
    internal float ScaleRatio { get; private set; } = 1;
    internal long AdjustedFrames { get; private set; }
    internal float MaximumScale => float.IsFinite(maximumScale) ? maximumScale : (float)Settings.MaximumCursorScale;
    static bool Exact(RenderFrame frame, Settings s) => s.AppearanceFor((int)frame.ShapeB).ExactSize && (frame.Morph >= .999f || s.AppearanceFor((int)frame.ShapeA).ExactSize);
    static float BaseScale(RenderFrame frame, Settings settings) => (float)(settings.AppearanceFor((int)frame.ShapeA).Scale * (1 - frame.Morph) + settings.AppearanceFor((int)frame.ShapeB).Scale * frame.Morph);
    internal RenderFrame Constrain(RenderFrame frame, Settings settings, Settings geometry, uint dpi)
    {
        if (!ReferenceEquals(settings, source) || context != dpi) { maximumScale = float.PositiveInfinity; source = settings; context = dpi; }
        if (Exact(frame, geometry)) { ScaleRatio = 1; return frame; }
        float baseScale = BaseScale(frame, geometry);
        ScaleRatio = baseScale > 0 ? Math.Min(baseScale, maximumScale) / baseScale : 1;
        return frame with { Scale = frame.Scale * ScaleRatio };
    }
    internal sealed record Prepared(RenderFrame Frame, Native.Rect Bounds, NativeCursorFile.Sprite Sprite);
    internal Prepared Prepare(GpuRenderer renderer, NativeCursorPresenter presenter, RenderFrame frame, Settings settings, NativeMotionBuilder motion, bool compact, uint? transportVendorId = null, int? transportSide = null)
    {
        uint vendor = transportVendorId ?? renderer.AdapterVendorId;
        int side = transportSide ?? NativeCursorLimits.Side(vendor);
        bool exact = Exact(frame, settings);
        Native.Rect bounds = default;
        NativeCursorFile.Sprite? Draw(RenderFrame pose)
        {
            bounds = NativeCursorPresenter.Bounds(pose, settings);
            renderer.Render(pose, settings, bounds);
            return presenter.PrepareFrame(renderer.Pixels(bounds.Width, bounds.Height), bounds, compact, vendor, side);
        }
        var sprite = Draw(frame);
        if (sprite != null && !(side == 128 && !exact && frame.Exposure == null && frame.Samples == 1 && Math.Max(presenter.RequiredWidth, presenter.RequiredHeight) > NativeCursorLimits.NvidiaGlyphSide)) return new(frame, bounds, sprite);
        AdjustedFrames++;
        float pulse = frame.Scale / Math.Max(.001f, Math.Min(BaseScale(frame, settings), maximumScale));
        var body = frame with { Start = Vector2.Zero, Control = Vector2.Zero, End = Vector2.Zero, Exposure = null, Samples = 1, Scale = frame.Scale / Math.Max(.001f, pulse) };
        Draw(body);
        // Reserve room for the visible motion integral when a large glyph
        // consumes the transport. Keep this cap stable on subsequent frames.
        int headBudget = side == 128 && !exact ? NativeCursorLimits.NvidiaGlyphSide : side;
        int bodyExtent = Math.Max(presenter.RequiredWidth, presenter.RequiredHeight);
        if (bodyExtent > headBudget)
        {
            float fit = Math.Clamp(.97f * headBudget / bodyExtent, .05f, .97f);
            maximumScale = Math.Min(maximumScale, body.Scale * fit);
            frame = frame with { Scale = frame.Scale * fit }; ScaleRatio *= fit;
        }
        for (int attempt = 0; attempt < 12; attempt++)
        {
            sprite = Draw(frame);
            if (sprite != null) return new(frame, bounds, sprite);
            if (frame.Exposure is { })
                frame = motion.FitAgain(frame, presenter.RequiredWidth, presenter.RequiredHeight, side, settings.MaxSamples);
            else if (frame.Samples > 1 && (frame.Start != Vector2.Zero || frame.Control != Vector2.Zero))
                frame = frame with { Start = frame.Start * .7f, Control = frame.Control * .7f };
            else
            {
                float fit = Math.Clamp(.95f * side / Math.Max(presenter.RequiredWidth, presenter.RequiredHeight), .05f, .95f);
                maximumScale = Math.Min(maximumScale, frame.Scale / Math.Max(.001f, pulse) * fit);
                frame = frame with { Scale = frame.Scale * fit }; ScaleRatio *= fit;
            }
        }
        throw new InvalidOperationException("Не удалось вместить курсор в совместимый кадр записи.");
    }
}
