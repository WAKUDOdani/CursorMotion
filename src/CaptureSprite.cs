namespace CursorMotion;

// Capture compatibility: remove ONLY transparent padding, keep every visible
// pixel and the exact hotspot. Never expose NVIDIA to oversized native images.
internal sealed class CaptureSprite
{
    byte[] pixels = Array.Empty<byte>();
    internal int RequiredWidth { get; private set; }
    internal int RequiredHeight { get; private set; }
    internal NativeCursorFile.Sprite? Prepare(byte[] source, Native.Rect bounds, bool stableCanvas = false, int maximumSide = 256)
    {
        if (maximumSide is not (128 or 192 or 256)) throw new ArgumentOutOfRangeException(nameof(maximumSide));
        int hotX = -bounds.Left, hotY = -bounds.Top;
        int left = hotX, top = hotY, right = hotX + 1, bottom = hotY + 1;
        for (int y = 0; y < bounds.Height; y++) for (int x = 0; x < bounds.Width; x++)
            if (source[(y * bounds.Width + x) * 4 + 3] != 0)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        RequiredWidth = right - left; RequiredHeight = bottom - top;
        int side = stableCanvas ? maximumSide : 64;
        while (side < Math.Max(right - left, bottom - top) && side < maximumSide) side = Math.Min(side * 2, maximumSide);
        if (right - left > side || bottom - top > side) return null;
        int offsetX = (side - (right - left)) / 2, offsetY = (side - (bottom - top)) / 2;
        if (pixels.Length != side * side * 4) pixels = new byte[side * side * 4]; else Array.Clear(pixels);
        for (int row = top; row < bottom; row++)
            Buffer.BlockCopy(source, (row * bounds.Width + left) * 4, pixels, ((row - top + offsetY) * side + offsetX) * 4, (right - left) * 4);
        return new(side, side, hotX - left + offsetX, hotY - top + offsetY, pixels);
    }
}
