using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class NativePresenterChecks
{
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
    public static void Run(string output)
    {
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        presenter.RefreshRoles();
        Require(presenter.RoleFor(Native.LoadCursor(0, 32512)) == 32512, "Arrow role lookup failed");
        Require(presenter.RoleFor(Native.LoadCursor(0, 32513)) == 32513, "Text role lookup failed");
        var tests = new List<object>();
        foreach (var pack in CursorPacks.All)
        foreach (int shape in Enumerable.Range(0, 8))
        foreach (bool moving in new[] { false, true })
        {
            var settings = CursorPacks.Apply(new Settings { Scale = moving ? 6 : 1.6 }, pack.Id);
            var raw = new Vector2(-1500, 1200);
            var frame = new RenderFrame(raw - (moving ? new Vector2(190, 80) : Vector2.Zero), raw - (moving ? new Vector2(100, 140) : Vector2.Zero), raw,
                (float)settings.AppearanceFor(shape).Scale, moving ? .18f : 0, shape, moving ? (shape + 1) % 4 : shape, moving ? .4f : 1, .37f, 1, moving ? 128 : 1);
            var relative = NativeCursorPresenter.Relative(frame, raw);
            var bounds = NativeCursorPresenter.Bounds(relative, settings);
            gpu.Render(relative, settings, bounds);
            var pixels = gpu.Pixels(bounds.Width, bounds.Height);
            nint cursor = presenter.CreatePrivateCursor(pixels, bounds.Width, bounds.Height, -bounds.Left, -bounds.Top);
            try {
                var sprite = new NativeCursorFile.Sprite(bounds.Width, bounds.Height, -bounds.Left, -bounds.Top, pixels);
                NativeCursorChecks.VerifyWindowsPixels(cursor, sprite);
                NativeCursorChecks.VerifyWindowsMask(cursor, sprite);
            }
            finally { Native.DestroyCursor(cursor); }
            tests.Add(new { pack = pack.Name, shape, moving, bounds.Width, bounds.Height });
        }
        foreach (int side in new[] { 512, 1024, 2048 })
        {
            byte[] pixels = new byte[side * side * 4];
            pixels[(side / 2 * side + side / 2) * 4 + 3] = 255;
            nint cursor = presenter.CreatePrivateCursor(pixels, side, side, side / 2, side / 2);
            try {
                var sprite = new NativeCursorFile.Sprite(side, side, side / 2, side / 2, pixels);
                NativeCursorChecks.VerifyWindowsPixels(cursor, sprite);
                NativeCursorChecks.VerifyWindowsMask(cursor, sprite);
            }
            finally { Native.DestroyCursor(cursor); }
        }
        // Use the exact production copy path, without calling SetSystemCursor.
        // All role slots must expose the SAME intermediate image and hotspot.
        Require(NativeCursorPresenter.PrimaryRoles.SequenceEqual(new uint[] { 32512, 32513, 32514, 32650, 32649, 32644, 32645, 32642, 32643 }), "Missing synchronized primary role");
        var publicationSettings = new Settings();
        var publicationFrame = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 2, 0, 0, 3, .42f, 0, 1, 1);
        var publicationBounds = NativeCursorPresenter.Bounds(publicationFrame, publicationSettings);
        gpu.Render(publicationFrame, publicationSettings, publicationBounds);
        var publicationPixels = gpu.Pixels(publicationBounds.Width, publicationBounds.Height);
        nint[] publication = presenter.CreatePublication(publicationPixels, publicationBounds.Width, publicationBounds.Height, -publicationBounds.Left, -publicationBounds.Top, 9);
        try
        {
            Require(publication.Distinct().Count() == 9, "Publication reused a consumed cursor handle");
            foreach (nint cursor in publication)
            {
                var sprite = new NativeCursorFile.Sprite(publicationBounds.Width, publicationBounds.Height, -publicationBounds.Left, -publicationBounds.Top, publicationPixels);
                NativeCursorChecks.VerifyWindowsPixels(cursor, sprite);
                NativeCursorChecks.VerifyWindowsMask(cursor, sprite);
            }
        }
        finally { foreach (nint cursor in publication) Native.DestroyCursor(cursor); }
        // Reuse the same canvas for a moving one-pixel glyph. The second frame
        // must update its mask without mutating the first cursor's private copy.
        byte[] firstPixels = new byte[257 * 73 * 4]; firstPixels[(11 * 257 + 19) * 4 + 3] = 255;
        byte[] secondPixels = new byte[firstPixels.Length]; secondPixels[(52 * 257 + 230) * 4 + 3] = 128;
        nint firstCursor = presenter.CreatePrivateCursor(firstPixels, 257, 73, 0, 0);
        nint secondCursor = 0;
        try
        {
            secondCursor = presenter.CreatePrivateCursor(secondPixels, 257, 73, 0, 0);
            NativeCursorChecks.VerifyWindowsMask(firstCursor, new(257, 73, 0, 0, firstPixels));
            NativeCursorChecks.VerifyWindowsMask(secondCursor, new(257, 73, 0, 0, secondPixels));
            NativeCursorChecks.VerifyWindowsPixels(firstCursor, new(257, 73, 0, 0, firstPixels));
            NativeCursorChecks.VerifyWindowsPixels(secondCursor, new(257, 73, 0, 0, secondPixels));
        }
        finally { Native.DestroyCursor(firstCursor); if (secondCursor != 0) Native.DestroyCursor(secondCursor); }
        // All-transparent frames must remain transparent (idle-hide), including AND mask fallback.
        byte[] transparent = new byte[64 * 64 * 4];
        nint empty = presenter.CreatePrivateCursor(transparent, 64, 64, 32, 32);
        try { NativeCursorChecks.VerifyWindowsPixels(empty, new(64, 64, 32, 32, transparent)); }
        finally { Native.DestroyCursor(empty); }
        var rawFrame = new RenderFrame(new(5, 10), new(5, 10), new(5, 10), 1.6f, 0, 0, 0, 1, 0, 1, 1);
        var translated = rawFrame with { Start = rawFrame.Start + new Vector2(300, 800), Control = rawFrame.Control + new Vector2(300, 800), End = rawFrame.End + new Vector2(300, 800) };
        Require(NativeCursorPresenter.Relative(rawFrame, rawFrame.End) == NativeCursorPresenter.Relative(translated, translated.End), "Native motion redraws identical static frames");
        var watch = Stopwatch.StartNew();
        byte[] bench = new byte[160 * 160 * 4]; bench[3] = 255;
        for (int i = 0; i < 1000; i++) { nint handle = presenter.CreatePrivateCursor(bench, 160, 160, 80, 80); Native.DestroyCursor(handle); }
        File.WriteAllText(Path.Combine(output, "native-presenter.json"), JsonSerializer.Serialize(new { pass = true, tests, largeFrames = new[] { 512, 1024, 2048 }, maskVerified = true, synchronizedMaskCopies = 9, reusedOddWidthMaskVerified = true, privateHandleMs = watch.Elapsed.TotalMilliseconds / 1000, noGlobalChanges = true, noCursorHwnd = true }));
    }
}
