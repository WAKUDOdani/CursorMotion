using System.Text;
using System.Text.Json;

namespace CursorMotion;

internal static class BackendServer
{
    public static int Run(string[] args)
    {
        Console.InputEncoding = new UTF8Encoding(false); Console.OutputEncoding = new UTF8Encoding(false);
        bool diagnostic = args.Contains("--diagnostic");
        using var mutex = new Mutex(true, diagnostic ? "Local\\CursorMotion.Test." + Environment.ProcessId : "Local\\CursorMotion.Instance", out bool first);
        if (!first) { Console.WriteLine(JsonSerializer.Serialize(new { @event = "fatal", error = "Закрой предыдущую версию через Ctrl + Alt + F12 и открой CursorMotion снова." })); return 2; }
        using var session = new BackendSession(diagnostic, args.Contains("--start"));
        Console.WriteLine(JsonSerializer.Serialize(new { @event = "ready", data = session.Execute("get") }));
        string? line;
        while ((line = Console.ReadLine()) != null)
        {
            int id = 0;
            try
            {
                using var request = JsonDocument.Parse(line);
                var root = request.RootElement; id = root.GetProperty("id").GetInt32();
                string command = root.GetProperty("cmd").GetString()!;
                object result = command == "shutdown" ? new { } : session.Execute(command, root.TryGetProperty("data", out var payload) ? payload : default);
                Console.WriteLine(JsonSerializer.Serialize(new { id, ok = true, data = result }));
                if (command == "shutdown") return 0;
            }
            catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { id, ok = false, error = ex.Message })); }
        }
        return 0;
    }
}
