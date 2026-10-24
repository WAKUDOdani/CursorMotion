using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace CursorMotion;

internal sealed class TrayHost : Forms.ApplicationContext
{
    readonly BackendSession session;
    readonly Forms.Control dispatch = new();
    readonly CancellationTokenSource stop = new();
    readonly Forms.NotifyIcon tray;
    readonly Forms.ContextMenuStrip menu = new();
    readonly HotkeyWindow hotkeys;
    readonly string pipeName;
    readonly bool diagnostic;
    readonly Task listener;
    bool exiting, disposed;
    Process? studio;
    static string PipeName => "CursorMotion." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value + ":" + Process.GetCurrentProcess().SessionId)))[..24];
    public static int Run(string[] args)
    {
        bool diagnostic = args.Contains("--diagnostic");
        int pipeIndex = Array.IndexOf(args, "--test-pipe");
        string pipe = diagnostic ? (pipeIndex >= 0 ? args[pipeIndex + 1] : "CursorMotion.Test." + Environment.ProcessId) : PipeName;
        if (diagnostic && (!pipe.StartsWith("CursorMotion.Test.", StringComparison.Ordinal) || pipe.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '.'))) throw new ArgumentException("Invalid diagnostic pipe");
        using var mutex = new Mutex(true, diagnostic ? "Local\\" + pipe : "Local\\CursorMotion.Instance", out bool first);
        if (!first)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                client.Connect(3000);
                using var timeout = new CancellationTokenSource(3000);
                using var reader = new StreamReader(client, new UTF8Encoding(false), leaveOpen: true);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                writer.WriteLine("{\"id\":1,\"cmd\":\"open\"}");
                reader.ReadLineAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                return 0;
            }
            catch
            {
                const string message = "Уже работает другая версия CursorMotion. Закрой её через Ctrl + Alt + F12 и запусти новую.";
                if (diagnostic) Console.Error.WriteLine(message); else System.Windows.MessageBox.Show(message, "CursorMotion");
                return 2;
            }
        }
        Forms.Application.SetHighDpiMode(Forms.HighDpiMode.PerMonitorV2);
        Forms.Application.EnableVisualStyles();
        using var host = new TrayHost(pipe, diagnostic, !args.Contains("--paused"));
        if (diagnostic)
        {
            int output = Array.IndexOf(args, "--output");
            if (output >= 0) File.WriteAllText(args[output + 1], JsonSerializer.Serialize(new { pipe, pid = Environment.ProcessId }));
        }
        else if (!args.Contains("--startup") && !args.Contains("--tray")) host.OpenSettings();
        Forms.Application.Run(host);
        return 0;
    }
    TrayHost(string pipeName, bool diagnostic, bool enabled)
    {
        this.pipeName = pipeName; this.diagnostic = diagnostic;
        session = new(diagnostic, enabled);
        dispatch.CreateControl();
        hotkeys = new(() => session.Engine.Enabled = !session.Engine.Enabled, ExitThread);
        if (!diagnostic) hotkeys.Register();
        tray = new() { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)!, Text = "CursorMotion", ContextMenuStrip = menu, Visible = !diagnostic };
        tray.DoubleClick += (_, _) => OpenSettings();
        menu.Opening += (_, _) => UpdateMenu();
        UpdateMenu();
        listener = Listen();
        Microsoft.Win32.SystemEvents.SessionEnding += SessionEnding;
    }
    void SessionEnding(object sender, Microsoft.Win32.SessionEndingEventArgs args)
    {
        session.Engine.Enabled = false;
        try { session.Flush(); } catch (Exception ex) { Program.Log(ex); }
        if (!dispatch.IsDisposed) dispatch.BeginInvoke(ExitThread);
    }
    void UpdateMenu()
    {
        menu.Items.Clear();
        menu.Items.Add("Открыть настройки", null, (_, _) => OpenSettings());
        var effect = new Forms.ToolStripMenuItem("Эффект включён") { Checked = session.Engine.Enabled };
        effect.Click += (_, _) => session.Engine.Enabled = !session.Engine.Enabled;
        menu.Items.Add(effect);
        var startup = new Forms.ToolStripMenuItem("Запускать вместе с Windows") { Checked = !diagnostic && StartupRegistration.Enabled, Enabled = !diagnostic };
        startup.Click += (_, _) => { try { StartupRegistration.Enabled = !StartupRegistration.Enabled; } catch (Exception ex) { ShowError(ex); } };
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        if (session.Engine.Failure is { } failure) menu.Items.Add("Эффект остановлен: " + failure).Enabled = false;
        menu.Items.Add("Восстановить курсор и выйти", null, (_, _) => ExitThread());
    }
    void ShowError(Exception error)
    {
        Program.Log(error);
        if (!diagnostic) { tray.BalloonTipTitle = "CursorMotion"; tray.BalloonTipText = error.Message; tray.ShowBalloonTip(6000); }
    }
    void OpenSettings()
    {
        if (diagnostic || exiting) return;
        try
        {
            string executable = Path.Combine(AppContext.BaseDirectory, "studio", "CursorMotion.Studio.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Не найдено окно настроек. Закрой CursorMotion и повторно запусти portable EXE.", executable);
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
            info.ArgumentList.Add("--pipe"); info.ArgumentList.Add(pipeName);
            var started = Process.Start(info);
            if (studio == null || studio.HasExited) { studio?.Dispose(); studio = started; } else started?.Dispose();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    async Task Listen()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false); }
                catch { pipe.Dispose(); throw; }
                _ = Serve(pipe);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { if (!exiting) Program.Log(ex); break; }
        }
    }
    async Task Serve(NamedPipeServerStream pipe)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(new { @event = "ready", data = session.Execute("get") }));
                while (!stop.IsCancellationRequested && await reader.ReadLineAsync(stop.Token) is { } line)
                {
                    int id = 0; bool shutdown = false;
                    string response;
                    try
                    {
                        if (line.Length > 1_048_576) throw new ArgumentException("Слишком большой запрос");
                        using var document = JsonDocument.Parse(line);
                        var root = document.RootElement; id = root.GetProperty("id").GetInt32();
                        string command = root.GetProperty("cmd").GetString()!;
                        JsonElement data = root.TryGetProperty("data", out var payload) ? payload : default;
                        object result;
                        if (command == "open") { dispatch.BeginInvoke(OpenSettings); result = new { }; }
                        else if (command == "startup")
                        {
                            if (!diagnostic && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("enabled", out var enabled)) StartupRegistration.Enabled = enabled.GetBoolean();
                            result = new { enabled = !diagnostic && StartupRegistration.Enabled, available = !diagnostic };
                        }
                        else if (command == "shutdown") { shutdown = true; result = new { }; }
                        else result = session.Execute(command, data);
                        response = JsonSerializer.Serialize(new { id, ok = true, data = result });
                    }
                    catch (Exception ex) { response = JsonSerializer.Serialize(new { id, ok = false, error = ex.Message }); }
                    await writer.WriteLineAsync(response);
                    if (shutdown) { dispatch.BeginInvoke(ExitThread); break; }
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            catch (Exception ex) { if (!exiting) Program.Log(ex); }
        }
    }
    protected override void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true; stop.Cancel();
        session.Engine.Enabled = false;
        try { session.Flush(); } catch (Exception ex) { Program.Log(ex); }
        tray.Visible = false;
        if (studio is { HasExited: false }) { try { studio.CloseMainWindow(); } catch { } }
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true;
            Microsoft.Win32.SystemEvents.SessionEnding -= SessionEnding;
            stop.Cancel();
            try { listener.Wait(1000); } catch { }
            session.Dispose(); hotkeys.Dispose(); tray.Icon?.Dispose(); tray.Dispose(); menu.Dispose(); dispatch.Dispose(); studio?.Dispose(); stop.Dispose();
        }
        base.Dispose(disposing);
    }
    sealed class HotkeyWindow(Action toggle, Action exit) : Forms.NativeWindow, IDisposable
    {
        public void Register()
        {
            CreateHandle(new Forms.CreateParams { Parent = new nint(-3) });
            if (!Native.RegisterHotKey(Handle, 1, 0x4003, 0x7B)) throw Native.Error("Не удалось зарегистрировать Ctrl + Alt + F12");
            if (!Native.RegisterHotKey(Handle, 2, 0x4003, 0x77)) { Native.UnregisterHotKey(Handle, 1); throw Native.Error("Не удалось зарегистрировать Ctrl + Alt + F8"); }
        }
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == 0x312) { if (message.WParam == 1) exit(); else if (message.WParam == 2) toggle(); }
            base.WndProc(ref message);
        }
        public void Dispose() { if (Handle != 0) { Native.UnregisterHotKey(Handle, 1); Native.UnregisterHotKey(Handle, 2); DestroyHandle(); } }
    }
}
