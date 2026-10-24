using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

// Replay the native presentation contract between application frames. No mouse,
// window or cursor APIs: Windows moves the image with its real hotspot while
// the last published bitmap offset remains unchanged until the next publication.
internal static class NativeTemporalChecks
{
    public static void Run(string output, bool enforce = false)
    {
        Directory.CreateDirectory(output); var records = new List<object>(); var traces = new List<object>();
        foreach (int fps in new[] { 60, 120, 240 })
        foreach (double smoothing in new[] { 0d, 140, 436, 500 })
        {
            var settings = new Settings { Smoothing = smoothing, Damping = 1, Shutter = 0, Scale = 4.2 };
            var logical = new Simulation(); var published = new Simulation();
            Vector2 offset = default, anchoredOffset = default; double nextPublication = 0, lastPublication = -.001;
            float maxCorrection = 0, maxError = 0, maxAnchoredCorrection = 0, maxAnchoredError = 0; double sumError = 0; int samples = 0;
            for (int i = 0; i <= 8000; i++)
            {
                double t = i / 1000d;
                var raw = new Vector2(1920 + 300 * (float)Math.Cos(t * 4 * Math.PI), 1080 + 300 * (float)Math.Sin(t * 4 * Math.PI));
                var input = new CursorInput(raw, true, 0, false);
                var desired = logical.Step(.001, input, settings);
                bool update = t + 1e-9 >= nextPublication;
                if (update)
                {
                    var frame = published.Step(t - lastPublication, input, settings);
                    var nextOffset = NativeCursorPresenter.Relative(frame, raw).End;
                    var nextAnchoredOffset = NativeCursorPresenter.Anchored(frame).End;
                    if (t > 3) maxCorrection = Math.Max(maxCorrection, Vector2.Distance(nextOffset, offset));
                    if (t > 3) maxAnchoredCorrection = Math.Max(maxAnchoredCorrection, Vector2.Distance(nextAnchoredOffset, anchoredOffset));
                    offset = nextOffset; anchoredOffset = nextAnchoredOffset; lastPublication = t; nextPublication += 1d / fps;
                }
                var displayed = raw + offset;
                if (t > 3)
                {
                    float error = Vector2.Distance(displayed, desired.End);
                    maxError = Math.Max(maxError, error); sumError += error * error; samples++;
                    maxAnchoredError = Math.Max(maxAnchoredError, Vector2.Distance(raw + anchoredOffset, raw));
                    if (fps == 120 && smoothing == 436 && t < 3.3)
                        traces.Add(new { t, update, rawX = raw.X, rawY = raw.Y, desiredX = desired.End.X, desiredY = desired.End.Y, displayedX = displayed.X, displayedY = displayed.Y });
                }
            }
            if (enforce && (maxAnchoredCorrection != 0 || maxAnchoredError != 0)) throw new InvalidOperationException("Native head detaches from the real hotspot between frames");
            if (enforce && smoothing > 0 && maxCorrection < 5) throw new InvalidOperationException("Temporal fixture did not expose legacy correction jitter");
            records.Add(new { fps, smoothing, maxPublicationCorrectionPx = maxCorrection, maxBetweenFrameErrorPx = maxError, rmsErrorPx = Math.Sqrt(sumError / samples), anchoredHeadCorrectionPx = maxAnchoredCorrection, anchoredBetweenFrameErrorPx = maxAnchoredError });
        }
        File.WriteAllText(Path.Combine(output, "native-temporal.json"), JsonSerializer.Serialize(new { noGlobalChanges = true, noRenderDelayModelled = true, records, traces }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
