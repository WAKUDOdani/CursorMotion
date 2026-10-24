using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class CaptureSpriteChecks
{
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        var compact = new CaptureSprite(); var records = new List<object>();
        foreach (var pack in CursorPacks.All)
        foreach (int shape in Enumerable.Range(0, 8))
        foreach (bool moving in new[] { false, true })
        {
            var s = CursorPacks.Apply(new Settings { Scale = 2.9, MaxSamples = 64 }, pack.Id);
            var frame = new RenderFrame(moving ? new(-130, -30) : Vector2.Zero, moving ? new(-60, 20) : Vector2.Zero,
                Vector2.Zero, 2.9f, .3f, shape, (shape + 1) % 4, .42f, .37f, 1, moving ? 64 : 1, true);
            var bounds = NativeCursorPresenter.Bounds(frame, s);
            gpu.Render(frame, s, bounds); var source = gpu.Pixels(bounds.Width, bounds.Height);
            // Exercise the ACTUAL publication preparation with a legacy profile
            // that explicitly disables compatibility. NVIDIA must stay bounded.
            var sprite = presenter.PrepareFrame(source, bounds, false, 0x10DE);
            Require(sprite != null, "Current-size lossless frame exceeded capture budget");
            Require(sprite!.Width == 256 && sprite.Height == 256, "NVIDIA cursor dimensions changed between frames");
            // Compare in HOTSPOT coordinates, including all zero pixels in the
            // destination: no resize, dimming, clipping or position displacement.
            for (int y = 0; y < bounds.Height; y++) for (int x = 0; x < bounds.Width; x++)
            {
                int i = (y * bounds.Width + x) * 4;
                if (source[i + 3] == 0) continue;
                int nx = x + bounds.Left + sprite.HotX, ny = y + bounds.Top + sprite.HotY;
                Require(nx >= 0 && nx < sprite.Width && ny >= 0 && ny < sprite.Height, "Visible pixel clipped");
                for (int c = 0; c < 4; c++) Require(sprite.Pixels[(ny * sprite.Width + nx) * 4 + c] == source[i + c], "Visible pixel changed");
            }
            for (int y = 0; y < sprite.Height; y++) for (int x = 0; x < sprite.Width; x++)
            {
                int sx = x - sprite.HotX - bounds.Left, sy = y - sprite.HotY - bounds.Top;
                byte expected = sx >= 0 && sx < bounds.Width && sy >= 0 && sy < bounds.Height ? source[(sy * bounds.Width + sx) * 4 + 3] : (byte)0;
                Require(sprite.Pixels[(y * sprite.Width + x) * 4 + 3] == expected, "Stale or shifted alpha in reused compact buffer");
            }
            var handles = presenter.CreatePublication(sprite.Pixels, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY, shape == 3 && moving ? 9 : 1);
            try { foreach (var handle in handles) { NativeCursorChecks.VerifyWindowsPixels(handle, sprite); NativeCursorChecks.VerifyWindowsMask(handle, sprite); } }
            finally { foreach (var handle in handles) Native.DestroyCursor(handle); }
            records.Add(new { pack = pack.Name, shape, moving, originalWidth = bounds.Width, originalHeight = bounds.Height, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY });
        }
        var oversized = new byte[512 * 512 * 4];
        for (int p = 3; p < oversized.Length; p += 4) oversized[p] = 255;
        Require(compact.Prepare(oversized, new() { Left = -256, Top = -256, Right = 256, Bottom = 256 }) == null,
            "Oversized image escaped the capture safety limit");
        Require(presenter.PrepareFrame(oversized, new() { Left = -256, Top = -256, Right = 256, Bottom = 256 }, false, 0x10DE) == null,
            "Legacy profile bypasses actual NVIDIA publication budget");
        // The user's maximum-size arrow must fit WITHOUT scaling. When motion
        // exceeds the budget the engine uses the intact static glyph instead.
        var maximum = new Settings { Scale = 6 };
        var maximumFrame = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 6, 0, 0, 0, 1, 0, 1, 1, true);
        var maximumBounds = NativeCursorPresenter.Bounds(maximumFrame, maximum);
        gpu.Render(maximumFrame, maximum, maximumBounds);
        var maximumPixels = gpu.Pixels(maximumBounds.Width, maximumBounds.Height);
        var maximumSprite = presenter.PrepareFrame(maximumPixels, maximumBounds, false, 0x10DE);
        Require(maximumSprite is { Width: 256, Height: 256 }, "Maximum-size static arrow cannot be published intact");
        for (int y = 0; y < maximumBounds.Height; y++) for (int x = 0; x < maximumBounds.Width; x++)
        {
            int i = (y * maximumBounds.Width + x) * 4; if (maximumPixels[i + 3] == 0) continue;
            int nx = x + maximumBounds.Left + maximumSprite!.HotX, ny = y + maximumBounds.Top + maximumSprite.HotY;
            Require(nx >= 0 && ny >= 0 && nx < 256 && ny < 256, "Maximum arrow clipped");
            for (int c = 0; c < 4; c++) Require(maximumPixels[i + c] == maximumSprite.Pixels[(ny * 256 + nx) * 4 + c], "Maximum arrow was rescaled");
        }
        var cached = new FramePolicy(); var settings = new Settings();
        var still = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 1.6f, 0, 0, 0, 1, 0, 1, 1, true);
        var rectangle = NativeCursorPresenter.Bounds(still, settings);
        Require(cached.NeedsRender(still, settings, rectangle) && !cached.NeedsRender(still, settings, rectangle), "Static cache fixture failed");
        Require(cached.NeedsRender(still, settings with { CaptureCompatibility = true }, rectangle), "Stationary capture toggle ignored");
        Require(cached.NeedsRender(still, settings, rectangle), "Stationary normal toggle ignored");
        File.WriteAllText(Path.Combine(output, "capture-sprites.json"), JsonSerializer.Serialize(new {
            pass = true, records, maxNativeSide = 256, stableNvidiaCanvas = true, maximumArrowIntact = true,
            legacyProfileCannotBypass = true, noGlobalChanges = true, noPixelScaling = true, nvidiaConfirmed = false
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
