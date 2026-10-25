using System.Reflection;
using System.Runtime.InteropServices;

namespace CursorMotion.Bootstrap;

// The same renderer is used by the splash and by offscreen visual checks.
// GDI+ writes antialiased premultiplied pixels directly into a layered DIB.
internal sealed class PreparationVisual : IDisposable
{
    internal const int LogicalWidth = 536, LogicalHeight = 282;
    internal readonly int Width, Height;
    readonly nint dc, dib, previous, bitmap, graphics, family, titleFont, subtitleFont, statusFont, smallFont, logo, logoStream;
    readonly nuint token;
    readonly byte[] shadow;
    bool disposed;
    internal readonly nint Bits;
    internal bool HasLogo => logo != 0;
    internal PreparationVisual(float scale)
    {
        Width = (int)Math.Ceiling(LogicalWidth * scale); Height = (int)Math.Ceiling(LogicalHeight * scale);
        shadow = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
        {
            double qx = Math.Abs((x + .5) / scale - 268) - 222, qy = Math.Abs((y + .5) / scale - 143) - 93;
            double distance = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2)) + Math.Min(Math.Max(qx, qy), 0) - 28;
            double alpha = 30 * Math.Exp(-Math.Pow(Math.Max(0, distance), 2) / 72);
            int i = (y * Width + x) * 4;
            shadow[i] = (byte)Math.Round(68 * alpha / 255); shadow[i + 1] = (byte)Math.Round(40 * alpha / 255);
            shadow[i + 2] = (byte)Math.Round(21 * alpha / 255); shadow[i + 3] = (byte)Math.Round(alpha);
        }
        try
        {
            var startup = new Gp.Startup { Version = 1 }; Gp.Check(Gp.GdiplusStartup(out token, ref startup, 0));
            dc = Native.CreateCompatibleDC(0);
            var info = new Native.BitmapInfo { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(), Width = Width, Height = -Height, Planes = 1, BitCount = 32 };
            dib = Native.CreateDIBSection(dc, ref info, 0, out var bits, 0, 0); Bits = bits;
            if (dc == 0 || dib == 0 || bits == 0) throw new InvalidOperationException("Cannot allocate preparation surface");
            previous = Native.SelectObject(dc, dib);
            Gp.Check(Gp.GdipCreateBitmapFromScan0(Width, Height, Width * 4, 0xE200B, bits, out bitmap));
            Gp.Check(Gp.GdipGetImageGraphicsContext(bitmap, out graphics));
            Gp.Check(Gp.GdipSetSmoothingMode(graphics, 4)); Gp.Check(Gp.GdipSetTextRenderingHint(graphics, 4));
            Gp.Check(Gp.GdipSetInterpolationMode(graphics, 7)); Gp.Check(Gp.GdipScaleWorldTransform(graphics, scale, scale, 0));
            foreach (string name in new[] { "Segoe UI Variable Text", "Segoe UI", "Arial" })
                if (Gp.GdipCreateFontFamilyFromName(name, 0, out family) == 0) break;
            if (family == 0) throw new InvalidOperationException("No preparation font available");
            titleFont = Font(25, 1); subtitleFont = Font(14, 0); statusFont = Font(12, 0); smallFont = Font(11, 0);
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorMotion.Preparation.Icon.png");
            if (resource != null)
            {
                using var data = new MemoryStream(); resource.CopyTo(data); byte[] bytes = data.ToArray();
                logoStream = Gp.SHCreateMemStream(bytes, (uint)bytes.Length);
                if (logoStream != 0) Gp.GdipLoadImageFromStream(logoStream, out logo);
            }
        }
        catch { Dispose(); throw; }
    }
    nint Font(float size, int style) { Gp.Check(Gp.GdipCreateFont(family, size, style, 2, out var font)); return font; }
    internal void Draw(PreparationState state, double time, bool reduced = false)
    {
        Marshal.Copy(shadow, 0, Bits, shadow.Length);
        Gradient(18, 16, 500, 242, 28, 0xFCFFFFFF, 0xF4E6EDFF);
        Stroke(18.7f, 16.7f, 498.6f, 240.6f, 27, 0xEDFFFFFF, 1.4f);
        Stroke(21, 19, 494, 236, 25, 0x289AC9FF, .8f);
        Round(40, 38, 64, 64, 18, 0x94FFFFFF);
        if (logo != 0) Gp.Check(Gp.GdipDrawImageRect(graphics, logo, 43, 41, 58, 58));
        else Text("CM", titleFont, 0xFF1687ED, 46, 50, 58, 40);
        Text("CursorMotion", titleFont, 0xFF17223B, 121, 39, 350, 39);
        Text("Подготовка к запуску", subtitleFont, 0xFF64738E, 123, 78, 330, 24);
        Text("Сейчас всё будет готово", subtitleFont, 0xFF283754, 41, 124, 448, 26);
        Text("Окно закроется автоматически.", smallFont, 0xFF76839B, 41, 151, 448, 23);
        float progress = (float)(state.Progress ?? 0);
        Round(42, 190, 452, 6, 3, 0x5FBDCCE7);
        if (state.Progress.HasValue && progress > 0)
            Gradient(42, 190, Math.Max(6, 452 * progress), 6, 3, 0xFF3B9EFF, 0xFF8C75F5);
        else if (!state.Progress.HasValue)
        {
            float position = reduced ? .35f : (float)(.5 + .5 * Math.Sin(time * 2.5 - Math.PI / 2));
            Gradient(42 + position * 320, 190, 132, 6, 3, 0xFF4BA4FF, 0xFF9282F8);
        }
        Text(state.Status, statusFont, 0xFF526581, 41, 208, 378, 28);
        if (state.Progress.HasValue) Text($"{Math.Round(progress * 100)}%", statusFont, 0xFF3979BE, 449, 208, 48, 28);
        Gp.Check(Gp.GdipFlush(graphics, 1));
    }
    void Text(string value, nint font, uint color, float x, float y, float width, float height)
    {
        Gp.Check(Gp.GdipCreateSolidFill(color, out var brush));
        try { var rect = new Gp.RectF { X = x, Y = y, Width = width, Height = height }; Gp.Check(Gp.GdipDrawString(graphics, value, value.Length, font, ref rect, 0, brush)); }
        finally { Gp.GdipDeleteBrush(brush); }
    }
    static nint Path(float x, float y, float width, float height, float radius)
    {
        Gp.Check(Gp.GdipCreatePath(0, out var path)); float d = Math.Min(radius * 2, Math.Min(width, height));
        Gp.GdipAddPathArc(path, x, y, d, d, 180, 90); Gp.GdipAddPathArc(path, x + width - d, y, d, d, 270, 90);
        Gp.GdipAddPathArc(path, x + width - d, y + height - d, d, d, 0, 90); Gp.GdipAddPathArc(path, x, y + height - d, d, d, 90, 90);
        Gp.Check(Gp.GdipClosePathFigure(path)); return path;
    }
    void Round(float x, float y, float width, float height, float radius, uint color)
    {
        var path = Path(x, y, width, height, radius); Gp.Check(Gp.GdipCreateSolidFill(color, out var brush));
        try { Gp.Check(Gp.GdipFillPath(graphics, brush, path)); } finally { Gp.GdipDeleteBrush(brush); Gp.GdipDeletePath(path); }
    }
    void Gradient(float x, float y, float width, float height, float radius, uint start, uint end)
    {
        var path = Path(x, y, width, height, radius); var rect = new Gp.RectF { X = x, Y = y, Width = width, Height = height };
        Gp.Check(Gp.GdipCreateLineBrushFromRect(ref rect, start, end, 0, 0, out var brush));
        try { Gp.Check(Gp.GdipFillPath(graphics, brush, path)); } finally { Gp.GdipDeleteBrush(brush); Gp.GdipDeletePath(path); }
    }
    void Stroke(float x, float y, float width, float height, float radius, uint color, float weight)
    {
        var path = Path(x, y, width, height, radius); Gp.Check(Gp.GdipCreatePen1(color, weight, 2, out var pen));
        try { Gp.Check(Gp.GdipDrawPath(graphics, pen, path)); } finally { Gp.GdipDeletePen(pen); Gp.GdipDeletePath(path); }
    }
    internal void Present(nint window, Native.Point destination)
    {
        var origin = new Native.Point(); var size = new Native.Size { Width = Width, Height = Height }; var blend = new Native.Blend { Alpha = 255, Format = 1 };
        if (!Native.UpdateLayeredWindow(window, 0, ref destination, ref size, dc, ref origin, 0, ref blend, 2)) throw new InvalidOperationException("Cannot present preparation surface");
    }
    internal void Save(string path)
    {
        var png = new Guid("557cf406-1a04-11d3-9a73-0000f81ef32e"); Gp.Check(Gp.GdipSaveImageToFile(bitmap, path, ref png, 0));
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (logo != 0) Gp.GdipDisposeImage(logo); if (logoStream != 0) Marshal.Release(logoStream);
        foreach (var font in new[] { titleFont, subtitleFont, statusFont, smallFont }) if (font != 0) Gp.GdipDeleteFont(font);
        if (family != 0) Gp.GdipDeleteFontFamily(family);
        if (graphics != 0) Gp.GdipDeleteGraphics(graphics); if (bitmap != 0) Gp.GdipDisposeImage(bitmap);
        if (previous != 0) Native.SelectObject(dc, previous); if (dib != 0) Native.DeleteObject(dib); if (dc != 0) Native.DeleteDC(dc);
        if (token != 0) Gp.GdiplusShutdown(token);
    }
}

internal static class Gp
{
    [StructLayout(LayoutKind.Sequential)] internal struct Startup { internal uint Version; internal nint Callback; internal int NoThread, NoCodecs; }
    [StructLayout(LayoutKind.Sequential)] internal struct RectF { internal float X, Y, Width, Height; }
    internal static void Check(int result) { if (result != 0) throw new InvalidOperationException("Preparation graphics failed: " + result); }
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdiplusStartup(out nuint token, ref Startup input, nint output);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern void GdiplusShutdown(nuint token);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateBitmapFromScan0(int w, int h, int stride, int format, nint pixels, out nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipGetImageGraphicsContext(nint image, out nint graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteGraphics(nint graphics);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDisposeImage(nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetSmoothingMode(nint graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetTextRenderingHint(nint graphics, int hint);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipSetInterpolationMode(nint graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipScaleWorldTransform(nint graphics, float x, float y, int order);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFlush(nint graphics, int mode);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipCreateFontFamilyFromName(string name, nint collection, out nint family);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteFontFamily(nint family);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateFont(nint family, float size, int style, int unit, out nint font);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteFont(nint font);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipDrawString(nint graphics, string text, int length, nint font, ref RectF bounds, nint format, nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateSolidFill(uint color, out nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteBrush(nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreateLineBrushFromRect(ref RectF rect, uint color1, uint color2, int mode, int wrap, out nint brush);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreatePath(int fillMode, out nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipClosePathFigure(nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePath(nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipFillPath(nint graphics, nint brush, nint path);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipCreatePen1(uint color, float width, int unit, out nint pen);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePen(nint pen);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawPath(nint graphics, nint pen, nint path);
    [DllImport("shlwapi.dll", ExactSpelling = true)] internal static extern nint SHCreateMemStream(byte[] bytes, uint length);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipLoadImageFromStream(nint stream, out nint image);
    [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDrawImageRect(nint graphics, nint image, float x, float y, float width, float height);
    [DllImport("gdiplus.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] internal static extern int GdipSaveImageToFile(nint image, string path, ref Guid encoder, nint parameters);
}
