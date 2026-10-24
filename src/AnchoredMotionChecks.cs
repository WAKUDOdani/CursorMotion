using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class AnchoredMotionChecks
{
    public static void Run(string output)
    {
        NativeTemporalChecks.Run(output, true);
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        var records = new List<object>();
        foreach (var pack in CursorPacks.All)
        foreach (int shape in Enumerable.Range(0, 8))
        {
            var settings = CursorPacks.Apply(new Settings { Scale = 4.2 }, pack.Id);
            var logical = new RenderFrame(new(1670, 850), new(1720, 830), new(1760, 890), 4.2f, .13f, shape, shape, 1, .37f, 1, 128);
            var frame = NativeCursorPresenter.Anchored(logical);
            Require(frame.End == Vector2.Zero && frame.LockHead && frame.Start == logical.Start - logical.End, "Anchoring changed local exposure or left positional lag");
            var bounds = NativeCursorPresenter.Bounds(frame, settings);
            gpu.Render(frame, settings, bounds); var moving = gpu.Pixels(bounds.Width, bounds.Height);
            if (pack.Id == 0 && shape == 0) gpu.SavePng(Path.Combine(output, "anchored-motion.png"), bounds.Width, bounds.Height);
            var sharpFrame = frame with { Start = Vector2.Zero, Control = Vector2.Zero, Samples = 1 };
            gpu.Render(sharpFrame, settings, bounds); var sharp = gpu.Pixels(bounds.Width, bounds.Height);
            int blurredCore = 0, trail = 0; double movingMass = 0, sharpMass = 0;
            for (int i = 0; i < moving.Length; i += 4)
            {
                movingMass += moving[i+3]; sharpMass += sharp[i+3];
                if (sharp[i+3] > 200 && moving[i+3] < sharp[i+3] - 30) blurredCore++;
                if (sharp[i+3] == 0 && moving[i+3] > 0) trail++;
                Require(moving[i] <= moving[i+3] + 1 && moving[i+1] <= moving[i+3] + 1 && moving[i+2] <= moving[i+3] + 1, "Whole-image exposure lost premultiplied alpha");
            }
            Require(trail > 10 && (pack.Id is 1 or 2 || blurredCore > 10), "Exposure does not blur the actual cursor body");
            Require(movingMass / sharpMass is > .8 and < 1.2, "Exposure adds an extra sharp icon or loses the normalized image");
            nint cursor = presenter.CreatePrivateCursor(moving, bounds.Width, bounds.Height, -bounds.Left, -bounds.Top);
            try
            {
                var sprite = new NativeCursorFile.Sprite(bounds.Width, bounds.Height, -bounds.Left, -bounds.Top, moving);
                NativeCursorChecks.VerifyWindowsPixels(cursor, sprite); NativeCursorChecks.VerifyWindowsMask(cursor, sprite);
            }
            finally { Native.DestroyCursor(cursor); }
            // LockHead cannot change static materials, transparency or artwork.
            gpu.Render(sharpFrame with { LockHead = false }, settings, bounds);
            Require(sharp.SequenceEqual(gpu.Pixels(bounds.Width, bounds.Height)), "Static native material changed in anchored mode");
            // At a dense, short exposure native output must be the same
            // normalized whole-image integral as the original renderer path.
            var shortBlur = frame with { Start = new(-12,0), Control = new(-6,0), Tilt = 0, Samples = 64 };
            gpu.Render(shortBlur, settings, bounds); var nativeBlur = gpu.Pixels(bounds.Width, bounds.Height);
            gpu.Render(shortBlur with { LockHead = false }, settings, bounds);
            Require(nativeBlur.SequenceEqual(gpu.Pixels(bounds.Width, bounds.Height)), "Native motion paints an extra layer over the original whole-image blur");
            records.Add(new { pack = pack.Name, shape, blurredCorePixels = blurredCore, visibleTrailPixels = trail, normalizedMassRatio = movingMass / sharpMass, bounds.Width, bounds.Height });
        }
        File.WriteAllText(Path.Combine(output, "anchored-motion.json"), JsonSerializer.Serialize(new { pass = true, noGlobalChanges = true, wholeCursorExposure = true, tests = records }, new JsonSerializerOptions { WriteIndented = true }));
    }
    static void Require(bool pass, string message) { if (!pass) throw new InvalidOperationException(message); }
}
