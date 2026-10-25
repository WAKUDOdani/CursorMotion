namespace CursorMotion;

// Active role is refreshed immediately during animation; synchronize the whole
// family at stable boundaries so a Windows role switch starts from the last
// settled visual state. Never queues old pointer frames.
internal sealed class NativePublicationPolicy
{
    bool valid, previousSettled;
    float previousMorph = 1;
    internal void Invalidate() => valid = false;
    internal bool AllRoles(RenderFrame frame, Settings settings)
    {
        bool settled = frame.Exposure == null && frame.Morph >= .999f && frame.ShapeB != 2
            && Math.Abs(frame.Scale - settings.AppearanceFor((int)frame.ShapeB).Scale) < .001
            && Math.Abs(frame.Tilt) < .001f && (frame.Opacity == 0 || frame.Opacity == 1);
        bool all = !valid || (frame.Morph >= .999f && previousMorph < .999f) || (settled && !previousSettled);
        valid = true; previousMorph = frame.Morph; previousSettled = settled;
        return all;
    }
}
