using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal static class GlassArtworkChecks
{
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        var records = new List<object>();
        foreach (var pack in CursorPacks.All)
        {
            var settings = CursorPacks.Apply(new Settings(), pack.Id);
            foreach (int shape in Enumerable.Range(0, 8))
            {
                var shot = NativeArtwork.Render(gpu, settings, shape);
                Require(shot.Width > 0 && shot.Height > 0 && shot.Core.left >= 0 && shot.Core.right <= shot.Width && shot.Core.top >= 0 && shot.Core.bottom <= shot.Height, "Invalid native artwork bounds");
                Require(shot.HotX >= 0 && shot.HotX < shot.Width && shot.HotY >= 0 && shot.HotY < shot.Height, "Artwork lost its real hotspot");
                byte[] png = shot.Png(); using var stream = new MemoryStream(png);
                var decoded = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var premult = new FormatConvertedBitmap(decoded, PixelFormats.Pbgra32, null, 0);
                byte[] actual = new byte[shot.Pixels.Length]; premult.CopyPixels(actual, shot.Width * 4, 0);
                Require(actual.Zip(shot.Pixels, (a, b) => Math.Abs(a - b)).Max() <= 1, "UI PNG does not reproduce native GPU pixels");
                nint cursor = presenter.CreatePrivateCursor(shot.Pixels, shot.Width, shot.Height, shot.HotX, shot.HotY);
                try { NativeCursorChecks.VerifyWindowsPixels(cursor, new(shot.Width, shot.Height, shot.HotX, shot.HotY, shot.Pixels)); }
                finally { Native.DestroyCursor(cursor); }
                records.Add(new { material = pack.Name, shape, shot.Width, shot.Height, pngBytes = png.Length });
            }
            var pos = new Vector2(100, 40); var frame = new RenderFrame(pos, pos, pos, 6, 0, 0, 0, 1, .37f, 1, 1);
            settings = settings.WithAppearance(0, settings.AppearanceFor(0) with { ShadowColor = "#00000000" });
            gpu.Render(frame, settings, new() { Right = 240, Bottom = 240 }); var pixels = gpu.Pixels(240, 240);
            if (pack.Id is 1 or 2)
            {
                int alpha = pixels[((40 + 6 * 6) * 240 + 100 + 3 * 6) * 4 + 3];
                Require(alpha is > 20 and < 200, "Glass body is opaque or invisible");
                Require(pixels.Where((_, i) => i % 4 == 3).Max() > alpha + 30, "Glass has no distinct reflective rim");
                for (int i = 0; i < pixels.Length; i += 4) Require(pixels[i] <= pixels[i + 3] + 1 && pixels[i + 1] <= pixels[i + 3] + 1 && pixels[i + 2] <= pixels[i + 3] + 1, "Glass is not premultiplied");
                var hidden = settings.WithAppearance(0, settings.AppearanceFor(0) with { FillColor = "#00000000", OutlineColor = "#00000000" });
                gpu.Render(frame, hidden, new() { Right = 240, Bottom = 240 });
                Require(gpu.Pixels(240, 240).All(value => value == 0), "Glass highlights ignore user opacity");
            }
            if (pack.Id == 0) Require(pixels[((40 + 6 * 6) * 240 + 100 + 3 * 6) * 4 + 3] == 255, "Classic became transparent");
        }
        File.WriteAllText(Path.Combine(output, "glass-artwork-results.json"), JsonSerializer.Serialize(new { pass = true, nativePngAndWindowsAgreement = true, records, noGlobalChanges = true }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
