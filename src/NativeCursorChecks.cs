using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CursorMotion;

internal static class NativeCursorChecks
{
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        using var renderer = new GpuRenderer();
        var summaries = new List<object>();
        foreach (var pack in CursorPacks.All)
        foreach (double scale in new[] { .5, 1.6, 6 })
        foreach (int shape in Enumerable.Range(0, 8))
        {
            var settings = CursorPacks.Apply(new Settings { Scale = scale }, pack.Id);
            var sprite = NativeCursorFile.Render(renderer, settings, shape, .37f);
            Require(sprite.Width is > 0 and <= 256 && sprite.Height is > 0 and <= 256, "Native dimensions exceed CUR limits");
            Require(sprite.Pixels.Where((_, i) => i % 4 == 3).Any(a => a > 32), "Native artwork is empty");
            string path = Path.Combine(output, $"native-{pack.Id}-{scale}-{shape}.cur");
            byte[] cur = NativeCursorFile.Encode(sprite); File.WriteAllBytes(path, cur);
            Require(BitConverter.ToUInt16(cur, 10) == sprite.HotX && BitConverter.ToUInt16(cur, 12) == sprite.HotY, "CUR hotspot differs from rendered anchor");
            nint cursor = Native.LoadImage(0, path, 2, 0, 0, 0x10);
            Require(cursor != 0, "Windows rejected native CUR");
            try
            {
                Require(Native.GetIconInfo(cursor, out var info), "Windows cannot inspect native CUR");
                try { Require(!info.Icon && info.HotX == sprite.HotX && info.HotY == sprite.HotY, "Windows changed native hotspot"); }
                finally { if (info.Mask != 0) Native.DeleteObject(info.Mask); if (info.Color != 0) Native.DeleteObject(info.Color); }
                VerifyWindowsPixels(cursor, sprite);
            }
            finally { Native.DestroyCursor(cursor); }
            summaries.Add(new { pack = pack.Name, shape, scale, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY });
        }
        foreach (var pack in CursorPacks.All)
        {
            string ani = Path.Combine(output, $"native-{pack.Id}-wait.ani");
            var settings = CursorPacks.Apply(new Settings(), pack.Id);
            File.WriteAllBytes(ani, NativeCursorFile.Build(renderer, settings, 2));
            nint cursor = Native.LoadImage(0, ani, 2, 0, 0, 0x10);
            Require(cursor != 0, "Windows rejected animated native wait cursor");
            try
            {
                VerifyWindowsPixels(cursor, NativeCursorFile.Render(renderer, settings, 2, 0));
                VerifyWindowsPixels(cursor, NativeCursorFile.Render(renderer, settings, 2, 12 / 30f), 12);
            }
            finally { Native.DestroyCursor(cursor); }
        }
        Require(ShellSurfacePolicy.IsShellSurface("TaskListThumbnailWnd", "explorer"), "Taskbar previews not covered");
        Require(ShellSurfacePolicy.IsShellSurface("Windows.UI.Core.CoreWindow", "ShellExperienceHost"), "Toast host not covered");
        Require(!ShellSurfacePolicy.IsShellSurface("CabinetWClass", "explorer"), "Regular Explorer should retain GPU effects");
        var a = new Settings();
        Require(NativeCursorTheme.SameAppearance(a, a with { Smoothing = 0, Fps = 240 }), "Motion edits rebuild native artwork");
        Require(!NativeCursorTheme.SameAppearance(a, a.WithAppearance(1, a.AppearanceFor(1) with { Scale = 3 })), "Text size edit ignored");
        // No SetSystemCursor/Magnification calls: these tests only create private handles.
        File.WriteAllText(Path.Combine(output, "native-cursor-results.json"), JsonSerializer.Serialize(new { pass = true, curves = summaries, animations = 5, globalCursorChanged = false }));
    }
    internal static void VerifyWindowsMask(nint cursor, NativeCursorFile.Sprite sprite)
    {
        nint dc = Native.CreateCompatibleDC(0), bitmap = 0, previous = 0;
        try
        {
            var info = new Native.BitmapInfo { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(), Width = sprite.Width, Height = -sprite.Height, Planes = 1, BitCount = 32 };
            bitmap = Native.CreateDIBSection(dc, ref info, 0, out nint bits, 0, 0);
            Require(bitmap != 0, "Mask test DIB creation failed"); previous = Native.SelectObject(dc, bitmap);
            Require(Native.DrawIconEx(dc, 0, 0, cursor, sprite.Width, sprite.Height, 0, 0, 1), "Windows failed to draw the cursor AND mask");
            byte[] actual = new byte[sprite.Pixels.Length]; Marshal.Copy(bits, actual, 0, actual.Length);
            for (int i = 0; i < actual.Length; i += 4)
            {
                int expected = sprite.Pixels[i + 3] == 0 ? 255 : 0;
                Require(actual[i] == expected && actual[i + 1] == expected && actual[i + 2] == expected,
                    "Cursor AND mask marks visible pixels transparent: software/masked composition cannot preserve the solid pointer");
            }
        }
        finally
        {
            if (previous != 0) Native.SelectObject(dc, previous);
            if (bitmap != 0) Native.DeleteObject(bitmap);
            Native.DeleteDC(dc);
        }
    }
    internal static void VerifyWindowsPixels(nint cursor, NativeCursorFile.Sprite sprite, uint frame = 0)
    {
        nint dc = Native.CreateCompatibleDC(0), bitmap = 0, previous = 0;
        try
        {
            var info = new Native.BitmapInfo { Size = (uint)Marshal.SizeOf<Native.BitmapInfo>(), Width = sprite.Width, Height = -sprite.Height, Planes = 1, BitCount = 32 };
            bitmap = Native.CreateDIBSection(dc, ref info, 0, out nint bits, 0, 0);
            Require(bitmap != 0, "Native test DIB creation failed"); previous = Native.SelectObject(dc, bitmap);
            Marshal.Copy(new byte[sprite.Pixels.Length], 0, bits, sprite.Pixels.Length);
            Require(Native.DrawIconEx(dc, 0, 0, cursor, sprite.Width, sprite.Height, frame, 0, 3), "Windows failed to draw private custom cursor");
            byte[] actual = new byte[sprite.Pixels.Length]; Marshal.Copy(bits, actual, 0, actual.Length);
            Require(actual.Zip(sprite.Pixels, (a, b) => Math.Abs(a - b)).Max() <= 3, "Windows native CUR pixels differ from GPU (alpha/color conversion)");
        }
        finally
        {
            if (previous != 0) Native.SelectObject(dc, previous);
            if (bitmap != 0) Native.DeleteObject(bitmap);
            Native.DeleteDC(dc);
        }
    }
}
