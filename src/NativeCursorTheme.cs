using System.IO;
using System.Numerics;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

// The native cursor remains above shell z-order bands. Its pixels come from the
// same SDF/material renderer as the effect, rather than from the application icon.
internal sealed class NativeCursorTheme
{
    Settings? applied;
    int? appliedSide;
    readonly CursorGuard guard;
    public int Generation { get; private set; }
    public NativeCursorTheme(CursorGuard guard) => this.guard = guard;
    internal static bool SameAppearance(Settings a, Settings b) => a.Style == b.Style
        && a.PreserveSpecialCursors == b.PreserveSpecialCursors
        && Enumerable.Range(0, 4).All(i => a.AppearanceFor(i) == b.AppearanceFor(i));

    public void Ensure(Settings settings, GpuRenderer renderer, int? transportSide = null)
    {
        if (applied != null && appliedSide == transportSide && SameAppearance(applied, settings)) return;
        var handles = new List<(uint Role, nint Handle)>();
        string folder = Path.Combine(Path.GetTempPath(), "CursorMotion-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var files = new string[8];
            for (int shape = 0; shape < files.Length; shape++)
            {
                int selected = settings.Style >= 0 ? settings.Style : shape;
                files[shape] = Path.Combine(folder, shape + (selected == 2 ? ".ani" : ".cur"));
                File.WriteAllBytes(files[shape], NativeCursorFile.Build(renderer, settings, selected, guard.Heartbeat, transportSide ?? NativeCursorLimits.Side(renderer.AdapterVendorId)));
            }
            var roles = NativeCursorPresenter.PrimaryRoles.Select(role => (Role: role, Shape: NativeCursorPresenter.ShapeFor(role))).ToList();
            if (!settings.PreserveSpecialCursors || settings.Style >= 0)
                foreach (uint role in new uint[] { 32515, 32516, 32646, 32648 }) roles.Add((role, 0));
            foreach (var (role, shape) in roles)
            {
                int fixedSide = NativeCursorLimits.RequiresCompact(renderer.AdapterVendorId) ? transportSide ?? NativeCursorLimits.Side(renderer.AdapterVendorId) : 0;
                nint handle = Native.LoadImage(0, files[shape], 2, fixedSide, fixedSide, 0x10);
                if (handle == 0) throw Native.Error("Load native custom cursor");
                handles.Add((role, handle));
            }
            guard.ArmThemeRecovery();
            // Restore untouched roles when switching back to PreserveSpecialCursors.
            if (applied != null)
            {
                guard.RestoreTheme(); guard.ArmThemeRecovery();
            }
            for (int i = 0; i < handles.Count; i++)
            {
                var (role, handle) = handles[i];
                // SetSystemCursor takes ownership and destroys the supplied handle.
                handles[i] = (role, 0);
                if (!Native.SetSystemCursor(handle, role)) throw Native.Error("Apply native custom cursor");
            }
            applied = settings;
            appliedSide = transportSide;
            Generation++;
        }
        finally
        {
            foreach (var (_, handle) in handles) if (handle != 0) Native.DestroyCursor(handle);
            try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public void Restore()
    {
        guard.RestoreTheme(); applied = null;
    }
}

internal static class NativeCursorFile
{
    internal sealed record Sprite(int Width, int Height, int HotX, int HotY, byte[] Pixels);
    public static Sprite Render(GpuRenderer renderer, Settings settings, int shape, float animation, int maximumSide = 256)
    {
        var appearance = settings.AppearanceFor(shape);
        int margin = (int)Math.Ceiling((25 + appearance.OutlineWidth + Math.Abs(appearance.ShadowX)
            + Math.Abs(appearance.ShadowY) + appearance.ShadowBlur * 2) * appearance.Scale + 3);
        int side = margin * 2;
        var point = new Vector2(margin, margin);
        var frame = new RenderFrame(point, point, point, (float)appearance.Scale, 0, shape, shape, 1, animation, 1, 1);
        renderer.Render(frame, settings, new() { Right = side, Bottom = side });
        byte[] source = renderer.Pixels(side, side);
        // Include the hotspot even for a completely transparent appearance.
        int left = margin, top = margin, right = margin + 1, bottom = margin + 1;
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
            if (source[(y * side + x) * 4 + 3] != 0)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        int width = right - left, height = bottom - top;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) Array.Copy(source, ((y + top) * side + left) * 4, pixels, y * width * 4, width * 4);
        int hotX = margin - left, hotY = margin - top;
        int glyphLimit = maximumSide == 128 && !appearance.ExactSize ? NativeCursorLimits.NvidiaGlyphSide : maximumSide;
        if (width > glyphLimit || height > glyphLimit)
        {
            // Rerender vector geometry rather than enlarging/reducing bitmap pixels.
            double scale = .97 * glyphLimit / Math.Max(width, height);
            return Render(renderer, settings.WithAppearance(shape, appearance with { Scale = appearance.Scale * scale }), shape, animation, maximumSide);
        }
        if (maximumSide == 128)
            return new CaptureSprite().Prepare(pixels, new() { Left = -hotX, Top = -hotY, Right = width - hotX, Bottom = height - hotY }, true, maximumSide)
                ?? throw new InvalidOperationException("Fallback cursor exceeded the recording frame.");
        return new(width, height, Math.Clamp(hotX, 0, width - 1), Math.Clamp(hotY, 0, height - 1), pixels);
    }
    public static byte[] Encode(Sprite sprite)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        int maskStride = (sprite.Width + 31) / 32 * 4;
        int imageLength = 40 + sprite.Width * sprite.Height * 4 + maskStride * sprite.Height;
        writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
        writer.Write((byte)(sprite.Width == 256 ? 0 : sprite.Width)); writer.Write((byte)(sprite.Height == 256 ? 0 : sprite.Height));
        writer.Write((ushort)0); writer.Write((ushort)sprite.HotX); writer.Write((ushort)sprite.HotY); writer.Write(imageLength); writer.Write(22);
        writer.Write(40); writer.Write(sprite.Width); writer.Write(sprite.Height * 2); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(0); writer.Write(sprite.Width * sprite.Height * 4); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        for (int y = sprite.Height - 1; y >= 0; y--) for (int x = 0; x < sprite.Width; x++)
        {
            int i = (y * sprite.Width + x) * 4; int a = sprite.Pixels[i + 3];
            // DIB file stores straight BGRA; the GPU returns premultiplied pixels.
            for (int c = 0; c < 3; c++) writer.Write((byte)(a == 0 ? 0 : Math.Min(255, (sprite.Pixels[i + c] * 255 + a / 2) / a)));
            writer.Write((byte)a);
        }
        for (int y = sprite.Height - 1; y >= 0; y--)
        {
            byte[] mask = new byte[maskStride];
            for (int x = 0; x < sprite.Width; x++) if (sprite.Pixels[(y * sprite.Width + x) * 4 + 3] == 0) mask[x / 8] |= (byte)(0x80 >> (x % 8));
            writer.Write(mask);
        }
        return stream.ToArray();
    }
    public static byte[] Build(GpuRenderer renderer, Settings settings, int shape, Action? heartbeat = null, int maximumSide = 256)
    {
        if (shape != 2) { heartbeat?.Invoke(); return Encode(Render(renderer, settings, shape, 0, maximumSide)); }
        using var payload = new MemoryStream(); using var writer = new BinaryWriter(payload);
        writer.Write(Encoding.ASCII.GetBytes("ACON"));
        using var header = new MemoryStream(); using (var hw = new BinaryWriter(header, Encoding.UTF8, true))
        { foreach (int value in new[] { 36, 24, 24, 0, 0, 32, 1, 2, 1 }) hw.Write(value); }
        Chunk(writer, "anih", header.ToArray());
        using var frames = new MemoryStream(); using (var fw = new BinaryWriter(frames, Encoding.UTF8, true))
        {
            fw.Write(Encoding.ASCII.GetBytes("fram"));
            for (int i = 0; i < 24; i++) { heartbeat?.Invoke(); Chunk(fw, "icon", Encode(Render(renderer, settings, shape, i / 30f, maximumSide))); }
        }
        Chunk(writer, "LIST", frames.ToArray());
        using var result = new MemoryStream(); using var rw = new BinaryWriter(result);
        rw.Write(Encoding.ASCII.GetBytes("RIFF")); rw.Write((uint)payload.Length); rw.Write(payload.ToArray()); return result.ToArray();
    }
    static void Chunk(BinaryWriter writer, string name, byte[] data)
    { writer.Write(Encoding.ASCII.GetBytes(name)); writer.Write((uint)data.Length); writer.Write(data); if ((data.Length & 1) != 0) writer.Write((byte)0); }
}
