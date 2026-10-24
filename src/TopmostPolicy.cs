namespace CursorMotion;

// Menu windows may enter the topmost band after the mouse button is released.
internal sealed class TopmostPolicy
{
    long burstUntil, lastRaise, lastEventRaise;
    bool wasRight, requested = true;
    public void Popup(long now) { requested = true; burstUntil = now + 350; }
    public bool WindowEvent(long now)
    {
        Popup(now);
        // SetWindowPos can produce reorder events; bound immediate re-raises.
        if (now - lastEventRaise < 16) return false;
        lastEventRaise = now; return true;
    }
    public bool ShouldRaise(long now, bool right)
    {
        if (right != wasRight) Popup(now);
        wasRight = right;
        if (requested || now <= burstUntil || now - lastRaise >= 150)
        { requested = false; lastRaise = now; return true; }
        return false;
    }
}
