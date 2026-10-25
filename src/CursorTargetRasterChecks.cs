using System.Numerics;
using System.Runtime.InteropServices;

namespace CursorMotion;

internal static class CursorTargetRasterChecks
{
    [StructLayout(LayoutKind.Sequential)] struct Bitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] static extern int GetObject(nint bitmap, int size, out Bitmap info);
    static string BitmapInfo(nint cursor)
    {
        Native.GetIconInfo(cursor, out var info);
        try { GetObject(info.Color, Marshal.SizeOf<Bitmap>(), out var bitmap); return $"{bitmap.Width}x{bitmap.Height},hot={info.HotX}/{info.HotY}"; }
        finally { if (info.Color != 0) Native.DeleteObject(info.Color); if (info.Mask != 0) Native.DeleteObject(info.Mask); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run(bool nativeCoordinates = false)
    {
        float ratio = CursorTargetRaster.Scale(48, 32);
        Require(Math.Abs(ratio - 2f / 3) < .000001f, "Target raster must follow cursor metrics, not a guessed resolution ratio");
        var settings = new Settings { Scale = 3.15, Shutter = 270, ShutterFps = 60, Smoothing = 0 };
        var target = CursorTargetRaster.Geometry(settings, ratio);
        Require(target.Shutter == settings.Shutter && target.ShutterFps == settings.ShutterFps && target.Smoothing == settings.Smoothing,
            "Target geometry changed accepted motion settings");
        var frame = new RenderFrame(new(-46, -12), new(-25, 9), Vector2.Zero, 3.15f, .15f, 0, 3, .4f, .25f, 1, 64, true);
        var mapped = CursorTargetRaster.Frame(frame, ratio);
        Require(mapped with { Scale = frame.Scale } == frame, "DPI raster mapping scaled or shifted the physical exposure path");
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        foreach (var pack in CursorPacks.All)
        foreach (int shape in Enumerable.Range(0, 8))
        {
            var look = CursorTargetRaster.Geometry(CursorPacks.Apply(settings, pack.Id), ratio);
            var pose = CursorTargetRaster.Frame(frame with { ShapeA = shape, ShapeB = shape, Morph = 1 }, ratio);
            var bounds = NativeCursorPresenter.Bounds(pose, look);
            gpu.Render(pose, look, bounds);
            var sprite = presenter.PrepareFrame(gpu.Pixels(bounds.Width, bounds.Height), bounds, true, 0x10DE, 256);
            Require(sprite != null, "Target-grid cursor exceeded the existing publication budget");
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) continue;
            nint[] copies = Array.Empty<nint>();
            void Verify()
            {
                for (int i = 0; i < copies.Length; i++)
                { try { NativeCursorChecks.VerifyWindowsPixels(copies[i], sprite!); NativeCursorChecks.VerifyWindowsMask(copies[i], sprite!); }
                  catch (Exception ex) { throw new InvalidOperationException($"pack={pack.Name}; shape={shape}; copy={i}; original={BitmapInfo(copies[0])}; actual={BitmapInfo(copies[i])}", ex); } }
            }
            try
            {
                using (var scope = new CursorTargetRaster.DpiScope(nativeCoordinates ? 1u : 96u))
                {
                    copies = presenter.CreatePublication(sprite!.Pixels, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY, 9);
                    if (!nativeCoordinates) Verify();
                }
                // NONE must keep exactly the same pixels outside the96-DPI
                // creator context as well: no hidden primary-DPI copy.
                if (nativeCoordinates) Verify();
            }
            finally { foreach (var copy in copies) Native.DestroyCursor(copy); }
        }
    }
}
