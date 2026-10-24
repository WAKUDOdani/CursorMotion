using System.IO;

namespace CursorMotion;

internal static class AppPaths
{
    public static string PublicExecutable
    {
        get
        {
            string? launcher = Environment.GetEnvironmentVariable("CURSORMOTION_LAUNCHER_EXE");
            return launcher != null && Path.IsPathFullyQualified(launcher) && File.Exists(launcher)
                && string.Equals(Path.GetExtension(launcher), ".exe", StringComparison.OrdinalIgnoreCase)
                ? launcher : Environment.ProcessPath!;
        }
    }
}
