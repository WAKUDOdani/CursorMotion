using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;

namespace CursorMotion;

internal static class CursorPublicationBenchmark
{
    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        using var gpu = new GpuRenderer(); using var presenter = new NativeCursorPresenter();
        var settings = new Settings { Scale = 3.15 };
        var pose = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 3.15f, 0, 0, 0, 1, 0, 1, 1, true);
        var bounds = NativeCursorPresenter.Bounds(pose, settings); gpu.Render(pose, settings, bounds);
        var sprite = presenter.PrepareFrame(gpu.Pixels(bounds.Width, bounds.Height), bounds, true, 0x10DE)!;
        var results = new List<object>();
        foreach (uint dpi in new uint[] { 0, 96, 1 })
        foreach (bool direct in new[] { false, true })
        {
            using var scope = new CursorTargetRaster.DpiScope(dpi);
            var samples = new List<double>();
            for (int i = 0; i < 32; i++)
            {
                long start = Stopwatch.GetTimestamp();
                var handles = presenter.CreatePublication(sprite.Pixels, sprite.Width, sprite.Height, sprite.HotX, sprite.HotY, 9, direct);
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                foreach (var handle in handles) Native.DestroyCursor(handle);
                if (i >= 8) samples.Add(ms);
            }
            results.Add(new { dpi, directHandles = direct, meanMs = samples.Average(), medianMs = samples.Order().ElementAt(samples.Count / 2), privateHandlesOnly = true });
        }
        File.WriteAllText(Path.Combine(output, "publication-benchmark.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
}
