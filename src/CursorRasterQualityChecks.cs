using System.Numerics;
using System.IO;
using System.Text.Json;

namespace CursorMotion;

internal static class CursorRasterQualityChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    internal static void Run(string output)
    {
        Require(CursorRasterQuality.Footprint(48, 48) == 1 && CursorRasterQuality.Footprint(48, 32) == 1.5f,
            "Cursor footprint must follow discrete Windows cursor metrics");
        Require(CursorRasterQuality.Footprint(32, 48) == 1 && CursorRasterQuality.Footprint(0, 32) == 1,
            "Primary/upscaled/invalid metrics must preserve original coverage");
        using var gpu = new GpuRenderer();
        var settings = new Settings { Scale = 6, ShadowColor = "#00000000" };
        var records = new List<object>();
        for (int shape = 0; shape < 8; shape++)
        {
            var frame = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, 6, 0, shape, shape, 1, .3f, 1, 1, true);
            var bounds = NativeCursorPresenter.Bounds(frame, settings);
            gpu.NativePixelFootprint = 1;
            gpu.Render(frame, settings, bounds); var original = gpu.Pixels(bounds.Width, bounds.Height);
            gpu.NativePixelFootprint = 1.5f;
            gpu.Render(frame, settings, bounds); var filtered = gpu.Pixels(bounds.Width, bounds.Height);
            int originalEdges = 0, filteredEdges = 0, silhouetteChanges = 0, solidChanges = 0;
            for (int i = 3; i < original.Length; i += 4)
            {
                int a = original[i], b = filtered[i];
                if (a > 0 && a < 255) originalEdges++;
                if (b > 0 && b < 255) filteredEdges++;
                if ((a >= 128) != (b >= 128)) silhouetteChanges++;
                if (a == 255 && b < 128) solidChanges++;
            }
            Require(filteredEdges > originalEdges, "Secondary monitor coverage did not prefilter vector edges");
            Require(silhouetteChanges <= 4 && solidChanges == 0, "Monitor edge filtering changed the cursor silhouette or opacity");
            gpu.NativePixelFootprint = 1;
            gpu.Render(frame, settings, bounds);
            Require(original.AsSpan().SequenceEqual(gpu.Pixels(bounds.Width, bounds.Height)), "Returning to primary left secondary coverage cached");
            records.Add(new { shape, originalEdges, filteredEdges, silhouetteChanges, solidChanges });
        }
        File.WriteAllText(Path.Combine(output, "monitor-edge-coverage.json"), JsonSerializer.Serialize(records));
    }
}
