using System.Numerics;
using System.Runtime.InteropServices;

namespace CursorMotion;

// No visible HWND and no global pointer hiding. Windows presents the GPU-rendered
// cursor itself, including its blur/morph/click frame, above ordinary desktop UI.
internal sealed class NativeCursorPresenter : IDisposable
{
    nint color, mask, bits;
    int width, height;
    byte[] straightPixels = Array.Empty<byte>();
    byte[] maskPixels = Array.Empty<byte>();
    readonly CaptureSprite captureSprite = new();
    static readonly uint[] roles = { 32512, 32513, 32514, 32650, 32515, 32516, 32642, 32643, 32644, 32645, 32646, 32648, 32649 };
    readonly nint[] shared = new nint[roles.Length];
    internal static readonly uint[] PrimaryRoles = { 32512, 32513, 32514, 32650, 32649, 32644, 32645, 32642, 32643 };
    public void RefreshRoles()
    { for (int i = 0; i < roles.Length; i++) shared[i] = Native.LoadCursor(0, (nint)roles[i]); }
    public uint RoleFor(nint cursor)
    { for (int i = 0; i < shared.Length; i++) if (shared[i] != 0 && shared[i] == cursor) return roles[i]; return 0; }
    public string Describe(nint cursor)
    {
        var info = new Native.IconInfoEx { Size = (uint)Marshal.SizeOf<Native.IconInfoEx>() };
        bool ok = Native.GetIconInfoEx(cursor, ref info);
        if (info.Mask != 0) Native.DeleteObject(info.Mask);
        if (info.Color != 0) Native.DeleteObject(info.Color);
        return $"handle={cursor:X}; iconInfo={ok}; resource={info.Resource}; module={info.Module}; name={info.Name}; hot={info.HotX},{info.HotY}; stock={string.Join(',', shared.Select(h => h.ToString("X")))}";
    }
    public static int ShapeFor(uint role) => role switch { 32513 => 1, 32514 or 32650 => 2, 32649 => 3, 32644 => 4, 32645 => 5, 32642 => 6, 32643 => 7, _ => 0 };
    public static uint RoleForShape(int shape) => shape switch { 1 => 32513, 2 => 32514, 3 => 32649, 4 => 32644, 5 => 32645, 6 => 32642, 7 => 32643, _ => 32512 };
    public static bool IsSpecial(uint role) => !PrimaryRoles.Contains(role);
    public static RenderFrame Relative(RenderFrame frame, Vector2 raw) => frame with
    { Start = frame.Start - raw, Control = frame.Control - raw, End = frame.End - raw };
    // Windows moves a native image with the real mouse between our frames.
    // Never encode positional spring lag into that image: it creates a sawtooth
    // correction at every publication. Keep only the filtered local exposure.
    public static RenderFrame Anchored(RenderFrame frame) => frame with
    { Start = frame.Start - frame.End, Control = frame.Control - frame.End, End = Vector2.Zero, LockHead = true };
    public static Native.Rect Bounds(RenderFrame frame, Settings settings)
    {
        Native.Rect bounds;
        if (frame.LockHead)
        {
            float scale = frame.Scale; double outline = 0, softness = 0, x = 0, y = 0;
            // Reserve one family-wide envelope. A role change or the animation
            // itself must not resize/reanchor the native bitmap just because
            // Arrow is 4.2x while Text/Hand are 1.6x. No per-frame allocations.
            for (int i = 0; i < 4; i++)
            {
                var look = settings.AppearanceFor(i); scale = Math.Max(scale, (float)look.Scale);
                outline = Math.Max(outline, look.OutlineWidth); softness = Math.Max(softness, look.ShadowBlur);
                x = Math.Max(x, Math.Abs(look.ShadowX)); y = Math.Max(y, Math.Abs(look.ShadowY));
            }
            float margin = GpuRenderer.AppearanceMargin(scale, outline, softness, x, y);
            var min = Vector2.Min(frame.Start, Vector2.Min(frame.Control, frame.End)) - new Vector2(margin);
            var max = Vector2.Max(frame.Start, Vector2.Max(frame.Control, frame.End)) + new Vector2(margin);
            bounds = new() { Left = (int)Math.Floor(min.X), Top = (int)Math.Floor(min.Y), Right = (int)Math.Ceiling(max.X), Bottom = (int)Math.Ceiling(max.Y) };
        }
        else bounds = GpuRenderer.Bounds(frame, settings, new() { Left = -8192, Top = -8192, Right = 8192, Bottom = 8192 });
        // Include the actual Windows hotspot at (0,0), even while the spring lags.
        bounds.Left = Math.Min(bounds.Left, 0); bounds.Top = Math.Min(bounds.Top, 0);
        bounds.Right = Math.Max(bounds.Right, 1); bounds.Bottom = Math.Max(bounds.Bottom, 1);
        bounds.Left = (int)Math.Floor(bounds.Left / 32d) * 32; bounds.Top = (int)Math.Floor(bounds.Top / 32d) * 32;
        bounds.Right = (int)Math.Ceiling(bounds.Right / 32d) * 32; bounds.Bottom = (int)Math.Ceiling(bounds.Bottom / 32d) * 32;
        return bounds;
    }
    public nint CreatePrivateCursor(byte[] pixels, int newWidth, int newHeight, int hotX, int hotY)
    {
        if (newWidth <= 0 || newHeight <= 0 || newWidth > 2048 || newHeight > 2048 || pixels.Length != newWidth * newHeight * 4
            || hotX < 0 || hotX >= newWidth || hotY < 0 || hotY >= newHeight) throw new ArgumentException("Invalid native frame bounds/hotspot");
        if (width != newWidth || height != newHeight || color == 0)
        {
            ReleaseBitmaps();
            var info = new Native.BitmapInfo { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(), Width = newWidth, Height = -newHeight, Planes = 1, BitCount = 32 };
            color = Native.CreateDIBSection(0, ref info, 0, out bits, 0, 0);
            maskPixels = new byte[(newWidth + 15) / 16 * 2 * newHeight]; Array.Fill(maskPixels, (byte)255);
            mask = Native.CreateBitmap(newWidth, newHeight, 1, 1, maskPixels);
            if (color == 0 || mask == 0) throw Native.Error("Create native cursor frame bitmap");
            width = newWidth; height = newHeight;
            straightPixels = new byte[pixels.Length];
        }
        // CreateIconIndirect's cursor conversion expects straight BGRA, as CUR DIB does.
        Array.Fill(maskPixels, (byte)255);
        int maskStride = (width + 15) / 16 * 2;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            int alpha = pixels[i + 3]; straightPixels[i + 3] = (byte)alpha;
            for (int c = 0; c < 3; c++) straightPixels[i + c] = (byte)(alpha == 0 ? 0 : Math.Min(255, (pixels[i + c] * 255 + alpha / 2) / alpha));
            // Same AND contract as the exported CUR: visible pixel -> 0;
            // transparent pixel -> 1. DI_NORMAL alpha drawing alone hid this
            // error; all-ones masks invert/lose the pointer in masked fallback.
            if (alpha != 0) maskPixels[y * maskStride + x / 8] &= (byte)~(0x80 >> (x % 8));
        }
        if (Native.SetBitmapBits(mask, (uint)maskPixels.Length, maskPixels) != maskPixels.Length) throw Native.Error("Update native cursor AND mask");
        Marshal.Copy(straightPixels, 0, bits, pixels.Length);
        var cursorInfo = new Native.IconInfo { HotX = (uint)hotX, HotY = (uint)hotY, Color = color, Mask = mask };
        nint cursor = Native.CreateIconIndirect(ref cursorInfo);
        if (cursor == 0) throw Native.Error("Create GPU system cursor frame");
        return cursor;
    }
    internal nint[] CreatePublication(byte[] pixels, int width, int height, int hotX, int hotY, int count)
    {
        var handles = new nint[count];
        try
        {
            handles[0] = CreatePrivateCursor(pixels, width, height, hotX, hotY);
            for (int i = 1; i < count; i++)
            {
                handles[i] = Native.CopyImage(handles[0], 2, 0, 0, 0);
                if (handles[i] == 0) throw Native.Error("Copy native cursor publication");
            }
            return handles;
        }
        catch { foreach (nint handle in handles) if (handle != 0) Native.DestroyCursor(handle); throw; }
    }
    internal NativeCursorFile.Sprite? PrepareFrame(byte[] pixels, Native.Rect bounds, bool requested, uint vendorId)
    {
        bool compact = NativeCursorLimits.UseCompact(requested, vendorId);
        return compact ? captureSprite.Prepare(pixels, bounds, NativeCursorLimits.RequiresCompact(vendorId))
            : new NativeCursorFile.Sprite(bounds.Width, bounds.Height, -bounds.Left, -bounds.Top, pixels);
    }
    public bool Present(GpuRenderer renderer, Native.Rect bounds, uint role, bool captureCompatibility = false)
    {
        // Windows changes role before our next tick. Keep the primary slots on the
        // same visual frame, so a role change cannot expose an old/finished glyph.
        // Roles remain separate HCURSORs, allowing detection without input hooks.
        uint[] targets = PrimaryRoles.Where(r => r != role).Append(role).ToArray();
        byte[] pixels = renderer.Pixels(bounds.Width, bounds.Height);
        var sprite = PrepareFrame(pixels, bounds, captureCompatibility, renderer.AdapterVendorId);
        if (sprite == null) return false;
        var handles = CreatePublication(sprite.Pixels, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY, targets.Length);
        try
        {
            for (int i = 0; i < targets.Length; i++)
            {
                nint handle = handles[i]; handles[i] = 0; // SetSystemCursor consumes ownership.
                if (!Native.SetSystemCursor(handle, targets[i])) throw Native.Error("Present GPU system cursor frame");
            }
        }
        finally { foreach (nint handle in handles) if (handle != 0) Native.DestroyCursor(handle); }
        return true;
    }
    void ReleaseBitmaps()
    {
        if (color != 0) Native.DeleteObject(color);
        if (mask != 0) Native.DeleteObject(mask);
        color = mask = bits = 0; width = height = 0;
    }
    public void Dispose() => ReleaseBitmaps();
}
