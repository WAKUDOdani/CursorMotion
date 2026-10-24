using System.IO;
using System.Windows;

namespace CursorMotion;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            GpuRenderer.AllowSoftwareForDiagnostics = args.Contains("--offscreen") || args.Contains("--diagnostic");
            GpuRenderer.ForceSoftwareForDiagnostics = args.Contains("--warp") && GpuRenderer.AllowSoftwareForDiagnostics;
            if (args.Contains("--enable-autostart")) { StartupRegistration.Enabled = true; return 0; }
            if (args.Contains("--disable-autostart")) { StartupRegistration.Enabled = false; return 0; }
            if (args.Contains("--backend")) return BackendServer.Run(args);
            if (args.Contains("--transition-check")) { int i = Array.IndexOf(args, "--output"); TransitionChecks.Run(i >= 0 ? args[i + 1] : "transitions", args.Contains("--enforce")); return 0; }
            if (args.Contains("--fx-readability-check")) { int i = Array.IndexOf(args, "--output"); FxReadabilityChecks.Run(i >= 0 ? args[i + 1] : "fx-readability", args.Contains("--enforce")); return 0; }
            if (args.Contains("--native-temporal-check")) { int i = Array.IndexOf(args, "--output"); NativeTemporalChecks.Run(i >= 0 ? args[i + 1] : "native-temporal"); return 0; }
            if (args.Contains("--motion-stress")) { int i = Array.IndexOf(args, "--output"); MotionStressChecks.Run(i >= 0 ? args[i + 1] : "motion-stress", args.Contains("--enforce")); return 0; }
            if (args.Contains("--export-native-art")) { int i = Array.IndexOf(args, "--output"); NativeArtwork.Export(i >= 0 ? args[i + 1] : "native-art"); return 0; }
            if (args.Contains("--artwork-fingerprint")) { int i = Array.IndexOf(args, "--output"); File.WriteAllText(i >= 0 ? args[i + 1] : "artwork-fingerprint.json", System.Text.Json.JsonSerializer.Serialize(NativeArtwork.Provenance())); return 0; }
            if (args.Contains("--export-pack-art")) { int i = Array.IndexOf(args, "--output"); PackArtExporter.Export(i >= 0 ? args[i + 1] : "pack-art"); return 0; }
            if (args.Contains("--export-hand-art")) { int i = Array.IndexOf(args, "--output"); HandChecks.ExportArt(i >= 0 ? args[i + 1] : "hand-art"); return 0; }
            if (args.Length == 3 && args[0] == "--watchdog") return CursorGuard.Watch(int.Parse(args[1]), args[2]);
            if (args.Contains("--restore")) { CursorGuard.Restore(); return 0; }
            if (args.Length == 2 && args[0] == "--guard-probe")
            {
                using var guard = new CursorGuard(); guard.SetHidden(true);
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { guard.StateName, guard.WatchdogId }));
                Thread.Sleep(Timeout.Infinite); return 0;
            }
            if (args.Contains("--self-test")) return SelfTest.Run(args);

            if (args.Contains("--render-gallery")) { int i = Array.IndexOf(args, "--output"); GraphicsChecks.Gallery(i >= 0 ? args[i + 1] : "gallery"); return 0; }
            if (args.Contains("--compile-shaders")) { int i = Array.IndexOf(args, "--output"); GpuRenderer.SaveCompiledShaders(i >= 0 ? args[i + 1] : "shaders"); return 0; }
            return TrayHost.Run(args);
        }
        catch (Exception ex)
        {
            Log(ex);
            bool diagnostic = args.Contains("--diagnostic") || args.Contains("--offscreen") || args.Contains("--compile-shaders") || args.Contains("--export-native-art") || args.Contains("--artwork-fingerprint") || args.Contains("--motion-stress") || args.Contains("--native-temporal-check") || args.Contains("--fx-readability-check") || args.Contains("--transition-check");
            if (!diagnostic) CursorGuard.Restore();
            if (args.Contains("--backend")) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { @event = "fatal", error = ex.Message }));
            else if (diagnostic) Console.Error.WriteLine(ex);
            else MessageBox.Show(ex.ToString(), "CursorMotion — ошибка запуска");
            return 1;
        }
    }
    internal static void Log(Exception ex)
    {
        try { Directory.CreateDirectory(Settings.DataDirectory); File.AppendAllText(Path.Combine(Settings.DataDirectory, "errors.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch { }
    }
}
