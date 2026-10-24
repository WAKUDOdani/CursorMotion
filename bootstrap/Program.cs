using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CursorMotion.Bootstrap;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool diagnostic = args.Contains("--diagnostic") || args.Contains("--offscreen") || args.Contains("--bootstrap-info");
        try
        {
            using var manifestStream = Resource("CursorMotion.Payload.json");
            using var manifest = JsonDocument.Parse(manifestStream);
            var root = manifest.RootElement;
            string payloadHash = root.GetProperty("sha256").GetString()!;
            var files = root.GetProperty("files").EnumerateArray().Select(entry => new PayloadFile(
                entry.GetProperty("path").GetString()!, entry.GetProperty("bytes").GetInt64(), entry.GetProperty("sha256").GetString()!)).ToArray();
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorMotion");
            string cacheBase = Path.Combine(data, "runtime", payloadHash);
            int customCache = Array.IndexOf(args, "--bootstrap-cache");
            if (customCache >= 0)
            {
                if (!diagnostic || customCache + 1 >= args.Length) throw new ArgumentException("Custom cache is diagnostic-only");
                cacheBase = Path.Combine(Path.GetFullPath(args[customCache + 1]), payloadHash);
            }
            string runtime = PayloadCache.Ensure(cacheBase, payloadHash, files, () => Resource("CursorMotion.Payload.zip"), !diagnostic);
            string publicExe = Environment.ProcessPath ?? throw new InvalidOperationException("Не найден portable EXE.");
            int infoIndex = Array.IndexOf(args, "--bootstrap-info");
            if (infoIndex >= 0)
            {
                if (infoIndex + 1 >= args.Length) throw new ArgumentException("Missing bootstrap report path");
                using var report = File.Create(args[infoIndex + 1]); using var writer = new Utf8JsonWriter(report);
                writer.WriteStartObject(); writer.WriteString("version", root.GetProperty("version").GetString());
                writer.WriteString("publicExe", publicExe); writer.WriteString("runtime", runtime); writer.WriteString("data", data);
                writer.WriteString("payloadHash", payloadHash); writer.WriteNumber("files", files.Length);
                writer.WriteBoolean("verified", true); writer.WriteBoolean("globalCursorChanged", false);
                writer.WriteString("startupCommand", $"\"{publicExe}\" --startup"); writer.WriteEndObject();
                return 0;
            }
            var start = new ProcessStartInfo(Path.Combine(runtime, "CursorMotion.Engine.exe"))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = runtime };
            bool backend = args.Contains("--backend");
            if (backend) { start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true; }
            start.Environment["CURSORMOTION_LAUNCHER_EXE"] = publicExe;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--bootstrap-cache") { i++; continue; }
                start.ArgumentList.Add(args[i]);
            }
            using var child = Process.Start(start) ?? throw new InvalidOperationException("Не удалось запустить CursorMotion.");
            Task? stdout = null, stderr = null;
            if (backend)
            {
                _ = ForwardInput(child);
                stdout = child.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
                stderr = child.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
            }
            // Ordinary launches hand off to the tray host and use no extra idle
            // process/RAM. CLI/diagnostic invocations preserve pipes + exit code.
            bool wait = diagnostic || args.Contains("--backend") || args.Contains("--restore")
                || args.Contains("--enable-autostart") || args.Contains("--disable-autostart");
            if (!wait) return 0;
            child.WaitForExit();
            if (backend) Task.WaitAll(stdout!, stderr!);
            return child.ExitCode;
        }
        catch (Exception ex)
        {
            string message = "Не удалось запустить CursorMotion.\n\n" + ex.Message;
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorMotion");
                Directory.CreateDirectory(data); File.AppendAllText(Path.Combine(data, "bootstrap.log"), $"{DateTimeOffset.Now:O} {ex}\n");
            }
            catch { }
            if (diagnostic) Console.Error.WriteLine(message);
            else Native.MessageBox(0, message, "CursorMotion", 0x10);
            return 1;
        }
    }
    static Stream Resource(string name) => Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidDataException("В portable EXE отсутствует компонент: " + name);
    static async Task ForwardInput(Process child)
    {
        try { await Console.OpenStandardInput().CopyToAsync(child.StandardInput.BaseStream); }
        catch (IOException) { }
        finally { try { child.StandardInput.Close(); } catch (InvalidOperationException) { } catch (IOException) { } }
    }
}

internal sealed record PayloadFile(string Path, long Bytes, string Sha256);

internal static class PayloadCache
{
    internal static string Child(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains(':') || relative.Contains('\\') || relative.StartsWith('/')
            || relative.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("Invalid embedded file path");
        string fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Embedded file escaped runtime");
        return target;
    }
    static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    internal static bool Verify(string runtime, PayloadFile[] files)
    {
        if (!Directory.Exists(runtime) || IsLink(runtime)) return false;
        try
        {
            foreach (var file in files)
            {
                string path = Child(runtime, file.Path);
                if (!File.Exists(path) || IsLink(path) || new FileInfo(path).Length != file.Bytes) return false;
                for (string? parent = Path.GetDirectoryName(path); parent != null && parent.Length > runtime.Length; parent = Path.GetDirectoryName(parent))
                    if (IsLink(parent)) return false;
                using var stream = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    internal static string Ensure(string cache, string sha256, PayloadFile[] files, Func<Stream> archive, bool showProgress)
    {
        Directory.CreateDirectory(cache);
        if (IsLink(cache)) throw new IOException("Runtime cache must not be a link");
        using var gate = new Mutex(false, "Local\\CursorMotion.Unpack." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(cache))));
        bool acquired;
        try { acquired = gate.WaitOne(TimeSpan.FromSeconds(90)); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Подготовка другого запуска ещё не завершилась. Попробуй ещё раз.");
        try
        {
            string current = Path.Combine(cache, "current");
            if (File.Exists(current) && !IsLink(current))
            {
                string slot = File.ReadAllText(current).Trim();
                if (slot.Length == 32 && slot.All(char.IsAsciiHexDigit))
                {
                    string ready = Path.Combine(cache, slot);
                    if (Verify(ready, files)) return ready;
                }
            }
            // Repair in a fresh generation. Never delete/overwrite DLLs belonging
            // to a running host, and never touch the user's settings.
            string generation = Guid.NewGuid().ToString("N"), destination = Path.Combine(cache, generation);
            Directory.CreateDirectory(destination);
            using var progress = showProgress ? new PreparationWindow() : null;
            using (var source = archive())
            {
                if (!Convert.ToHexString(SHA256.HashData(source)).Equals(sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Контрольная сумма portable EXE не совпала. Скачай файл заново.");
                source.Position = 0;
                using var zip = new ZipArchive(source, ZipArchiveMode.Read);
                var expected = files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    if (!expected.TryGetValue(entry.FullName, out var file) || !seen.Add(entry.FullName) || entry.Length != file.Bytes)
                        throw new InvalidDataException("Unexpected embedded runtime file");
                    string target = Child(destination, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using (var input = entry.Open()) using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)) input.CopyTo(output);
                    progress?.Pump();
                }
                if (seen.Count != files.Length) throw new InvalidDataException("Incomplete embedded runtime");
            }
            if (!Verify(destination, files)) throw new InvalidDataException("Проверка компонентов после распаковки не прошла.");
            string pending = current + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(pending, generation); File.Move(pending, current, true);
            return destination;
        }
        finally { gate.ReleaseMutex(); }
    }
}

// A short native splash only during first extraction/repair. No Electron, no
// desktop capture, focus activation, input hooks or extra resident process.
internal sealed class PreparationWindow : IDisposable
{
    readonly nint window;
    internal PreparationWindow()
    {
        int width = 440, height = 112;
        window = Native.CreateWindowEx(0x08000080, "STATIC", "CursorMotion\n\nПодготовка первого запуска…", 0x90000201,
            (Native.GetSystemMetrics(0) - width) / 2, (Native.GetSystemMetrics(1) - height) / 2, width, height, 0, 0, 0, 0);
        if (window != 0) { Native.SendMessage(window, 0x30, Native.GetStockObject(17), 1); Native.UpdateWindow(window); }
    }
    internal void Pump() { while (Native.PeekMessage(out var message, 0, 0, 0, 1)) { Native.TranslateMessage(ref message); Native.DispatchMessage(ref message); } }
    public void Dispose() { if (window != 0) Native.DestroyWindow(window); }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { internal nint Hwnd; internal uint Id; internal nuint WParam; internal nint LParam; internal uint Time; internal Point Point; internal uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int MessageBox(nint owner, string text, string title, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint CreateWindowEx(uint exStyle, string className, string text, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] internal static extern nint GetStockObject(int index);
    [DllImport("user32.dll")] internal static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] internal static extern bool UpdateWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(ref Message message);
}
