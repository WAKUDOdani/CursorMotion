using Microsoft.Win32;

namespace CursorMotion;

internal static class StartupRegistration
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "CursorMotion";
    public static string Command(string executable) => $"\"{executable}\" --startup";
    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(Key); return string.Equals(key?.GetValue(Name) as string, Command(AppPaths.PublicExecutable), StringComparison.OrdinalIgnoreCase); }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            if (value) key.SetValue(Name, Command(AppPaths.PublicExecutable), RegistryValueKind.String);
            else key.DeleteValue(Name, false);
        }
    }
}
