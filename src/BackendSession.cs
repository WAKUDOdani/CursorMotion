using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CursorMotion;

internal sealed class BackendSession : IDisposable
{
    readonly object gate = new();
    readonly bool diagnostic;
    readonly string? profilePath;
    readonly bool persist;
    readonly Timer saveTimer;
    readonly Lazy<NativeArtworkService> artwork = new(() => new NativeArtworkService());
    readonly UserPresets presets;
    Settings settings;
    public OverlayEngine Engine { get; }
    public BackendSession(bool diagnostic, bool enabled, string? profilePath = null)
    {
        this.diagnostic = diagnostic;
        this.profilePath = profilePath; persist = !diagnostic || profilePath != null;
        settings = persist ? Settings.Load(profilePath) : new();
        string presetDirectory = diagnostic
            ? Path.Combine(profilePath is null ? Path.GetTempPath() : Path.GetDirectoryName(Path.GetFullPath(profilePath))!, "CursorMotion-presets-test-" + Guid.NewGuid().ToString("N"))
            : Path.Combine(Settings.DataDirectory, "presets");
        presets = new(presetDirectory);
        Engine = new(settings, startThread: !diagnostic) { Enabled = !diagnostic && enabled };
        saveTimer = new(_ => { try { Flush(); } catch (Exception ex) { Program.Log(ex); } }, null, Timeout.Infinite, Timeout.Infinite);
    }
    public object State() => new { enabled = Engine.Enabled, error = Engine.Failure, adapter = Engine.Status, fps = Engine.ActualFps, renderMs = Engine.RenderMilliseconds, presentation = Engine.Presentation, compactRequired = Engine.CompactCursorRequired, frames = Engine.RenderedFrames, diagnostics = Engine.Diagnostics };
    object Snapshot() => new { settings, appearances = Enumerable.Range(0, 4).Select(settings.AppearanceFor).ToArray(), packs = CursorPacks.All, presets = presets.Items.Select(item => new { id = item.Id, name = item.Name, updatedAt = item.UpdatedAt, isCurrent = item.Settings == settings, summary = new { scale = item.Settings.AppearanceFor(0).Scale, shutter = item.Settings.Shutter, smoothing = item.Settings.Smoothing } }).ToArray(), presetWarning = presets.Warning, state = State(), displays = Native.Displays().Select((display, index) => new { id = index, label = display.ToString() }).ToArray(), warning = diagnostic ? null : Settings.LoadWarning };
    public object Execute(string command, JsonElement data = default)
    {
        if (command == "artwork")
        {
            var looks = JsonSerializer.Deserialize<CursorAppearance[]>(data.GetProperty("appearances"), new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (looks is null || looks.Length != 4 || looks.Any(look => look is null)) throw new ArgumentException("Нужны четыре оформления для образцов");
            return artwork.Value.Render(looks.Select(look => look.Validate()).ToArray());
        }
        lock (gate)
        {
            object result;
            bool changed = false;
            switch (command)
            {
                case "get": return Snapshot();
                case "status": return State();
                case "enabled": Engine.Enabled = !diagnostic && data.GetProperty("value").GetBoolean(); return State();
                case "flush": if (persist) settings.Save(profilePath); return new { saved = true };
                case "set":
                    string property = data.GetProperty("property").GetString()!;
                    string[] allowed = { "Smoothing", "Damping", "Shutter", "TrailFalloff", "MaxSamples", "Tilt", "ClickScale", "Style", "Monitor", "CustomX", "CustomY", "CustomWidth", "CustomHeight", "Fps", "ShutterFps", "PreserveSpecialCursors", "WindowTint", "InterfaceParallax", "ReduceMotion", "CaptureCompatibility" };
                    if (!allowed.Contains(property)) throw new ArgumentException("Неизвестная настройка");
                    var updated = settings with { }; SetProperty(updated, property, data.GetProperty("value")); settings = updated; changed = true; result = new { }; break;
                case "appearance":
                    int shape = data.GetProperty("shape").GetInt32(); if (shape is < 0 or > 3) throw new ArgumentException("Неизвестная форма");
                    var look = settings.AppearanceFor(shape) with { }; SetProperty(look, data.GetProperty("property").GetString()!, data.GetProperty("value"));
                    settings = settings.WithAppearance(shape, look); changed = true; result = new { }; break;
                case "pack":
                    int? target = data.TryGetProperty("shape", out var shapeValue) && shapeValue.ValueKind != JsonValueKind.Null ? shapeValue.GetInt32() : null;
                    if (target is < 0 or > 3) throw new ArgumentException("Неизвестная форма");
                    settings = CursorPacks.Apply(settings, data.GetProperty("id").GetInt32(), target); changed = true; result = new { }; break;
                case "forms":
                    var forms = JsonSerializer.Deserialize<CursorAppearance[]>(data.GetProperty("appearances"), new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
                    // Three-entry batches from old clients preserve the fourth form.
                    if (forms is null || forms.Length is not (3 or 4) || forms.Any(form => form is null)) throw new ArgumentException("Нужны четыре формы курсора");
                    var next = settings;
                    var preservedHand = settings.AppearanceFor(3);
                    for (int index = 0; index < forms.Length; index++) next = next.WithAppearance(index, forms[index]);
                    if (forms.Length == 3) next = next.WithAppearance(3, preservedHand);
                    settings = next; changed = true; result = new { }; break;
                case "preset":
                    var motion = Settings.Preset(data.GetProperty("name").GetString()!);
                    settings = settings with { Smoothing = motion.Smoothing, Damping = motion.Damping, Shutter = motion.Shutter, TrailFalloff = motion.TrailFalloff, MaxSamples = motion.MaxSamples, Tilt = motion.Tilt, ClickScale = motion.ClickScale };
                    changed = true; result = new { }; break;
                case "presets-save":
                    presets.Save(data.GetProperty("name").GetString()!, settings,
                        data.TryGetProperty("id", out var presetId) && presetId.ValueKind == JsonValueKind.String ? presetId.GetString() : null);
                    return Snapshot();
                case "presets-apply": settings = presets.Get(data.GetProperty("id").GetString()!).Settings; changed = true; result = new { }; break;
                case "presets-delete": presets.Delete(data.GetProperty("id").GetString()!); return Snapshot();
                case "presets-import": presets.Import(data.GetProperty("path").GetString()!); return Snapshot();
                case "presets-export": presets.Get(data.GetProperty("id").GetString()!).Settings.Save(data.GetProperty("path").GetString()!); return new { saved = true };
                case "reset": settings = Settings.CreateDefault(); changed = true; result = new { }; break;
                case "import": settings = (JsonSerializer.Deserialize<Settings>(File.ReadAllText(data.GetProperty("path").GetString()!)) ?? throw new JsonException("Пустой профиль")).Validate(); changed = true; result = new { }; break;
                case "export": settings.Save(data.GetProperty("path").GetString()!); return new { saved = true };
                default: throw new ArgumentException("Неизвестная команда");
            }
            if (changed) { settings = settings.Validate(); Engine.Settings = settings; saveTimer.Change(500, Timeout.Infinite); result = Snapshot(); }
            return result;
        }
    }
    public void Flush() { lock (gate) { if (persist) settings.Save(profilePath); } }
    static void SetProperty(object target, string name, JsonElement value)
    {
        var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException("Неизвестный параметр");
        object converted = property.PropertyType == typeof(double) ? value.GetDouble() : property.PropertyType == typeof(int) ? value.GetInt32() : property.PropertyType == typeof(bool) ? value.GetBoolean() : value.GetString()!;
        property.SetValue(target, converted);
    }
    public void Dispose()
    {
        saveTimer.Dispose();
        try { Flush(); } finally { Engine.Dispose(); if (artwork.IsValueCreated) artwork.Value.Dispose(); }
    }
}
