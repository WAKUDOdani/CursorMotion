using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CursorMotion.Bootstrap;

internal static class CacheTests
{
    static int assertions;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "CursorMotion-BootstrapTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
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
