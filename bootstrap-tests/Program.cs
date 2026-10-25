using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CursorMotion.Bootstrap;

internal static class CacheTests
{
    static int assertions;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static void Main(string[] args)
    {
        string root = Path.Combine(Path.GetTempPath(), "CursorMotion-BootstrapTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string preview = args.Length == 2 && args[0] == "--output" ? Path.GetFullPath(args[1]) : Path.Combine(root, "preparation");
        Directory.CreateDirectory(preview);
        foreach (float scale in new[] { 1f, 1.75f, 2f })
        {
            using var visual = new PreparationVisual(scale);
            Check(visual.HasLogo, "Preparation app icon was not embedded");
            visual.Draw(new("Распаковываем компоненты…", .56), .5);
            visual.Save(Path.Combine(preview, $"preparation-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}x.png"));
            byte[] rgba = new byte[visual.Width * visual.Height * 4];
            System.Runtime.InteropServices.Marshal.Copy(visual.Bits, rgba, 0, rgba.Length);
            Check(rgba[3] == 0, "Preparation has opaque square corners");
            Check(Enumerable.Range(0, rgba.Length / 4).All(i => rgba[i * 4] <= rgba[i * 4 + 3] && rgba[i * 4 + 1] <= rgba[i * 4 + 3] && rgba[i * 4 + 2] <= rgba[i * 4 + 3]), "Preparation alpha is not premultiplied");
            visual.Draw(new("Проверяем компоненты…", null), 1);
            visual.Save(Path.Combine(preview, $"verification-{scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}x.png"));
        }
        var splash = new PreparationWindow(visible: false);
        try
        {
            SpinWait.SpinUntil(() => splash.Initialized || splash.Failure != null, 5000);
            Check(splash.Initialized && splash.Failure == null, "Hidden preparation window failed: " + splash.Failure);
            Check((splash.WindowStyle & 0x08000080) == 0x08000080 && !splash.WasVisible, "Preparation can activate or exposed a test window");
            splash.Report("Распаковываем компоненты…", .75); Thread.Sleep(80);
            Check(splash.Failure == null, "Preparation update failed");
        }
        finally { splash.Dispose(); }
        Check(splash.ThreadEnded, "Preparation UI thread survived disposal");
        byte[] data = Encoding.UTF8.GetBytes("Synthetic runtime, not an executable");
        var files = new[] { new PayloadFile("studio/resources/app/main.cjs", data.Length, Convert.ToHexString(SHA256.HashData(data))) };
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        using (var entry = zip.CreateEntry(files[0].Path).Open()) entry.Write(data);
        byte[] payload = buffer.ToArray(); string hash = Convert.ToHexString(SHA256.HashData(payload));
        Stream Archive() => new MemoryStream(payload, false);
        string first = PayloadCache.Ensure(root, hash, files, Archive, false);
        Check(PayloadCache.Verify(first, files), "Fresh payload not verified");
        Check(File.ReadAllBytes(PayloadCache.Child(first, files[0].Path)).SequenceEqual(data), "Extraction changed bytes");
        Check(PayloadCache.Ensure(root, hash, files, () => throw new Exception("Warm launch opened archive"), false) == first, "Warm launch did not reuse runtime");
        File.WriteAllText(PayloadCache.Child(first, files[0].Path), "corrupted cache");
        string repaired = PayloadCache.Ensure(root, hash, files, Archive, false);
        Check(repaired != first && PayloadCache.Verify(repaired, files), "Corrupt cache not repaired in a fresh generation");
        Check(Directory.Exists(first), "Repair removed files potentially used by an old host");
        foreach (string bad in new[] { "../outside", "/absolute", "C:/outside", "x/../../outside", "a\\b", "a:stream", "a//b" })
        {
            bool rejected = false; try { PayloadCache.Child(root, bad); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Unsafe archive path accepted: " + bad);
        }
        File.WriteAllText(Path.Combine(root, "current"), "../../outside");
        string pointerRepair = PayloadCache.Ensure(root, hash, files, Archive, false);
        Check(PayloadCache.Verify(pointerRepair, files) && pointerRepair != repaired, "Unsafe current pointer followed");
        bool invalidHash = false;
        try { PayloadCache.Ensure(Path.Combine(root, "bad-hash"), new string('0', 64), files, Archive, false); }
        catch (InvalidDataException) { invalidHash = true; }
        Check(invalidHash, "Corrupt archive hash accepted");
        bool invalidManifest = false;
        try { PayloadCache.Ensure(Path.Combine(root, "bad-manifest"), hash, new[] { files[0] with { Bytes = data.Length + 1 } }, Archive, false); }
        catch (InvalidDataException) { invalidManifest = true; }
        Check(invalidManifest, "Manifest size mismatch accepted");
        Check(!File.Exists(Path.Combine(root, "bad-manifest/current")), "Unverified extraction marked current");
        string concurrent = Path.Combine(root, "concurrent");
        var results = Enumerable.Range(0, 4).Select(_ => Task.Run(() => PayloadCache.Ensure(concurrent, hash, files, Archive, false))).ToArray();
        Task.WaitAll(results);
        Check(results.Select(task => task.Result).Distinct().Count() == 1, "Concurrent launches published multiple generations");
        Check(PayloadCache.Verify(results[0].Result, files), "Concurrent runtime is incomplete");
        Console.WriteLine($"Bootstrap cache passed: {assertions}; no cursor, autostart or installed profile changes.");
    }
}
