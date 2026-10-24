using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorMotion;

internal sealed record ArtworkBounds(int left, int top, int right, int bottom, int sampleStep = 1, int alphaThreshold = 32);
internal sealed record ArtworkMetadata(int width, int height, ArtworkBounds bounds);
internal sealed record NativeArtworkFrame(int Width, int Height, int HotX, int HotY, byte[] Pixels, ArtworkBounds Core)
{
    public ArtworkMetadata Metadata => new(Width, Height, Core);
    public byte[] Png()
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Pbgra32, null, Pixels, Width * 4)));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    public object Response() => new { renderer = "native-sdf", dataUri = "data:image/png;base64," + Convert.ToBase64String(Png()), width = Width, height = Height, bounds = Core };
}

// Still images from the production shader. Never creates a desktop HWND, cursor
// handle or theme, and never changes the user's profile or input coordinates.
internal static class NativeArtwork
{
    internal static readonly string[] Subjects = { "arrow", "text", "waiting", "hand", "resize-horizontal", "resize-vertical", "resize-nwse", "resize-nesw", "motion", "space" };
    public static NativeArtworkFrame Render(GpuRenderer gpu, Settings settings, int shape)
    {
        var appearance = settings.AppearanceFor(shape);
        float scale = (float)Math.Min(24, 900 / (24 + appearance.OutlineWidth + Math.Abs(appearance.ShadowX) + Math.Abs(appearance.ShadowY) + 2 * appearance.ShadowBlur));
        var frame = new RenderFrame(Vector2.Zero, Vector2.Zero, Vector2.Zero, scale, 0, shape, shape, 1, .37f, 1, 1);
        return RenderFrame(gpu, settings, frame);
    }
    internal static NativeArtworkFrame RenderFrame(GpuRenderer gpu, Settings settings, RenderFrame frame)
    {
        var bounds = NativeCursorPresenter.Bounds(frame, settings);
        gpu.Render(frame, settings, bounds); byte[] actual = gpu.Pixels(bounds.Width, bounds.Height);
        var profile = settings.AppearanceFor((int)frame.ShapeB);
        var maskSettings = settings.WithAppearance((int)frame.ShapeB, profile with { FillColor = "#FFFFFFFF", OutlineColor = "#FFFFFFFF", ShadowColor = "#00000000" });
        gpu.Render(frame, maskSettings, bounds); byte[] mask = gpu.Pixels(bounds.Width, bounds.Height);
        var core = Ink(mask, bounds.Width, bounds.Height, 32);
        var ink = Ink(actual, bounds.Width, bounds.Height, 0);
        int left = Math.Min(core.left, ink.left), top = Math.Min(core.top, ink.top);
        int right = Math.Max(core.right, ink.right), bottom = Math.Max(core.bottom, ink.bottom);
        int width = right - left, height = bottom - top;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) Array.Copy(actual, ((y + top) * bounds.Width + left) * 4, pixels, y * width * 4, width * 4);
        return new(width, height, -bounds.Left - left, -bounds.Top - top, pixels, new(core.left - left, core.top - top, core.right - left, core.bottom - top));
    }
    internal static ArtworkBounds Ink(byte[] pixels, int width, int height, int threshold)
    {
        int left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) if (pixels[(y * width + x) * 4 + 3] > threshold)
        { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        return right <= left ? new(width / 2, height / 2, width / 2 + 1, height / 2 + 1) : new(left, top, right, bottom);
    }
    public static void Export(string output)
    {
        Directory.CreateDirectory(output); using var gpu = new GpuRenderer();
        var metadata = new Dictionary<string, Dictionary<string, ArtworkMetadata>>();
        foreach (var pack in CursorPacks.All)
        {
            string material = pack.Name.ToLowerInvariant(), folder = Path.Combine(output, material);
            Directory.CreateDirectory(folder); var settings = CursorPacks.Apply(new Settings(), pack.Id);
            var items = metadata[material] = new();
            var primary = new List<NativeArtworkFrame>();
            for (int shape = 0; shape < 8; shape++)
            {
                var sprite = Render(gpu, settings, shape); File.WriteAllBytes(Path.Combine(folder, Subjects[shape] + ".png"), sprite.Png()); items[Subjects[shape]] = sprite.Metadata;
                if (shape < 4) primary.Add(sprite);
            }
            var group = Group(primary);
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Path.GetFullPath(output))!.FullName, material + ".png"), group.Png());
            if (pack.Id == 0) File.WriteAllBytes(Path.Combine(Directory.GetParent(Path.GetFullPath(output))!.FullName, "custom.png"), group.Png());
            var motion = RenderSubject(gpu, settings, 8);
            File.WriteAllBytes(Path.Combine(folder, "motion.png"), motion.Png()); items["motion"] = motion.Metadata;
            var space = Space(gpu, settings); File.WriteAllBytes(Path.Combine(folder, "space.png"), space.Png()); items["space"] = space.Metadata;
        }
        File.WriteAllText(Path.Combine(output, "native-artwork-metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "native-artwork-provenance.json"), JsonSerializer.Serialize(Provenance(), new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static object Provenance()
    {
        using var reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("CursorMotion.Cursor.hlsl")!);
        return new { renderer = "native-sdf", shaderSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reader.ReadToEnd()))),
            paletteSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(CursorPacks.All)))), enlargedSamples = true, desktopCapture = false };
    }
    static NativeArtworkFrame Group(List<NativeArtworkFrame> sprites)
    {
        const int width = 1536, height = 640; const double zoom = .75, gap = 44;
        var visual = new DrawingVisual();
        double span = sprites.Sum(sprite => (sprite.Core.right - sprite.Core.left) * zoom) + 3 * gap;
        double x = (width - span) / 2, baseline = (height + sprites.Max(sprite => (sprite.Core.bottom - sprite.Core.top) * zoom)) / 2;
        using (var dc = visual.RenderOpen()) foreach (var sprite in sprites)
        {
            dc.DrawImage(BitmapSource.Create(sprite.Width, sprite.Height, 96, 96, PixelFormats.Pbgra32, null, sprite.Pixels, sprite.Width * 4),
                new Rect(x - sprite.Core.left * zoom, baseline - sprite.Core.bottom * zoom, sprite.Width * zoom, sprite.Height * zoom));
            x += (sprite.Core.right - sprite.Core.left) * zoom + gap;
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        byte[] pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        return new(width, height, 0, 0, pixels, Ink(pixels, width, height, 32));
    }
    internal static NativeArtworkFrame RenderSubject(GpuRenderer gpu, Settings settings, int subject) => subject switch
    {
        8 => RenderFrame(gpu, settings, new(new(-220, 50), new(-100, -40), Vector2.Zero, 18, 0, 0, 0, 1, .37f, 1, 24)),
        9 => Space(gpu, settings),
        _ => Render(gpu, settings, subject)
    };
    static NativeArtworkFrame Space(GpuRenderer gpu, Settings settings)
    {
        const int width = 1536, height = 1024; var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            foreach (var (shape, x, y) in new[] { (0, 768, 470), (4, 300, 500), (5, 1236, 500), (6, 370, 800), (7, 1166, 800) })
            {
                var sprite = Render(gpu, settings, shape); var core = sprite.Core;
                double factor = shape == 0 ? 1 : .6;
                dc.DrawImage(BitmapSource.Create(sprite.Width, sprite.Height, 96, 96, PixelFormats.Pbgra32, null, sprite.Pixels, sprite.Width * 4),
                    new Rect(x - (core.left + core.right) * .5 * factor, y - (core.top + core.bottom) * .5 * factor, sprite.Width * factor, sprite.Height * factor));
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        byte[] pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        return new(width, height, 768, 470, pixels, Ink(pixels, width, height, 32));
    }
}

internal sealed class NativeArtworkService : IDisposable
{
    readonly BlockingCollection<(CursorAppearance[] Looks, TaskCompletionSource<object> Done)> jobs = new();
    readonly Thread thread;
    public NativeArtworkService()
    {
        thread = new(Run) { Name = "CursorMotion still samples", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }
    public object Render(CursorAppearance[] looks)
    {
        var done = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        jobs.Add((looks, done)); return done.Task.GetAwaiter().GetResult();
    }
    void Run()
    {
        GpuRenderer? gpu = null; var cache = new Dictionary<string, object>(); var order = new Queue<string>();
        try
        {
            while (!jobs.IsCompleted)
            {
                if (!jobs.TryTake(out var job, 1500)) { gpu?.Dispose(); gpu = null; continue; }
                try
                {
                    var frames = new Dictionary<string, object>();
                    var settings = new Settings { Arrow = job.Looks[0], Text = job.Looks[1], Wait = job.Looks[2], Hand = job.Looks[3] };
                    for (int shape = 0; shape < NativeArtwork.Subjects.Length; shape++)
                    {
                        var appearance = settings.AppearanceFor(shape) with { Scale = 1.6, MorphTime = 140, IdleHide = 0 };
                        string key = shape + ":" + JsonSerializer.Serialize(appearance);
                        if (!cache.TryGetValue(key, out var item))
                        {
                            gpu ??= new();
                            item = NativeArtwork.RenderSubject(gpu, settings, shape).Response(); cache.Add(key, item); order.Enqueue(key);
                            if (order.Count > 32) cache.Remove(order.Dequeue());
                        }
                        frames.Add(NativeArtwork.Subjects[shape], item);
                    }
                    job.Done.SetResult(new { renderer = "native-sdf", normalizedScale = true, frames });
                }
                catch (Exception ex) { job.Done.SetException(ex); }
            }
        }
        finally { gpu?.Dispose(); }
    }
    public void Dispose() { jobs.CompleteAdding(); if (thread.Join(2000)) jobs.Dispose(); }
}
