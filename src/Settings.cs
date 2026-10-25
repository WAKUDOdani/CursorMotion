using System.Globalization;
using System.IO;
using System.Text.Json;

namespace CursorMotion;

public sealed record Settings
{
    public const double MaximumCursorScale = 4.5;
    public int Version { get; init; } = 3;
    // Missing entries inherit the legacy appearance, preserving existing profiles exactly.
    public CursorAppearance? Arrow { get; init; }
    public CursorAppearance? Text { get; init; }
    public CursorAppearance? Wait { get; init; }
    public CursorAppearance? Hand { get; init; }
    public double Smoothing { get; init; } = 140;
    public double Damping { get; init; } = .85;
    public double Shutter { get; init; } = 270;
    public double TrailFalloff { get; init; } = .6;
    public double BlurDensity { get; init; } = 2;
    public int MaxSamples { get; init; } = 64;
    public double Tilt { get; init; } = 4;
    public double ClickScale { get; init; } = .85;
    public int Style { get; init; } = -1;
    public double Scale { get; init; } = 1.6;
    public string FillColor { get; init; } = "#FF000000";
    public string OutlineColor { get; init; } = "#FFFFFFFF";
    public string ShadowColor { get; init; } = "#66000000";
    public double OutlineWidth { get; init; } = 1.4;
    public double ShadowBlur { get; init; } = 2.5;
    public double ShadowX { get; init; } = 0;
    public double ShadowY { get; init; } = 1.5;
    public double MorphTime { get; init; } = 140;
    public double IdleHide { get; init; }
    // -3 = whole virtual desktop, -2 = primary, -1 = custom, >=0 = monitor.
    public int Monitor { get; init; } = -3;
    public int CustomX { get; init; }
    public int CustomY { get; init; }
    public int CustomWidth { get; init; } = 1920;
    public int CustomHeight { get; init; } = 1080;
    public int Fps { get; init; } = 120;
    // Blur duration remains independent of desktop refresh, as in a 60 fps OBS scene.
    public int ShutterFps { get; init; } = 60;
    public bool PreserveSpecialCursors { get; init; }
    public double WindowTint { get; init; } = .35;
    public double InterfaceParallax { get; init; } = 1;
    public bool ReduceMotion { get; init; }
    public bool CaptureCompatibility { get; init; }

    internal static double Limit(double value, double low, double high, double fallback) => double.IsFinite(value) ? Math.Clamp(value, low, high) : fallback;
    public Settings Validate() => this with
    {
        Version = 3, Arrow = Arrow?.Validate(), Text = Text?.Validate(), Wait = Wait?.Validate(), Hand = Hand?.Validate(),
        WindowTint = Limit(WindowTint, .15, .85, .35),
        InterfaceParallax = Limit(InterfaceParallax, 0, 2, 1),
        Smoothing = Limit(Smoothing, 0, 500, 140), Damping = Limit(Damping, .3, 1.5, .85),
        Shutter = Limit(Shutter, 0, 720, 270), TrailFalloff = Limit(TrailFalloff, 0, 1, .6),
        BlurDensity = Limit(BlurDensity, 0, 4, 2),
        MaxSamples = Math.Clamp(MaxSamples, 4, 128), Tilt = Limit(Tilt, 0, 20, 4),
        ClickScale = Limit(ClickScale, .5, 1, .85), Scale = Limit(Scale, .5, MaximumCursorScale, 1.6),
        Style = Math.Clamp(Style, -1, 3), OutlineWidth = Limit(OutlineWidth, 0, 4, 1.4),
        ShadowBlur = Limit(ShadowBlur, 0, 10, 2.5), ShadowX = Limit(ShadowX, -10, 10, 0), ShadowY = Limit(ShadowY, -10, 10, 1.5),
        MorphTime = Limit(MorphTime, 0, 500, 140), IdleHide = Limit(IdleHide, 0, 30, 0),
        CustomX = Math.Clamp(CustomX, -32768, 32768), CustomY = Math.Clamp(CustomY, -32768, 32768),
        CustomWidth = Math.Clamp(CustomWidth, 1, 32768), CustomHeight = Math.Clamp(CustomHeight, 1, 32768),
        Fps = Math.Clamp(Fps, 30, 240), ShutterFps = Math.Clamp(ShutterFps, 24, 240),
        FillColor = ValidColor(FillColor, "#FF000000"), OutlineColor = ValidColor(OutlineColor, "#FFFFFFFF"), ShadowColor = ValidColor(ShadowColor, "#66000000")
    };
    internal static string ValidColor(string? value, string fallback) => TryColor(value, out _) ? value! : fallback;
    public CursorAppearance AppearanceFor(int shape) => shape == 3 ? Hand ?? AppearanceFor(0) : (shape switch { 1 => Text, 2 => Wait, _ => Arrow }) ?? new CursorAppearance
    {
        Scale = Scale, FillColor = FillColor, OutlineColor = OutlineColor, ShadowColor = ShadowColor,
        OutlineWidth = OutlineWidth, ShadowBlur = ShadowBlur, ShadowX = ShadowX, ShadowY = ShadowY,
        MorphTime = MorphTime, IdleHide = IdleHide
    };
    public Settings WithAppearance(int shape, CursorAppearance appearance) => shape switch
    {
        1 => this with { Text = appearance.Validate() }, 2 => this with { Wait = appearance.Validate() },
        3 => this with { Hand = appearance.Validate() },
        _ => this with { Arrow = appearance.Validate(), Hand = Hand ?? AppearanceFor(3) }
    };
    public static bool TryColor(string? value, out uint color)
    {
        color = 0;
        return value is { Length: 9 } && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);
    }
    public static bool PortableMode => File.Exists(Path.Combine(AppContext.BaseDirectory, "portable.flag"));
    public static string DataDirectory => PortableMode ? Path.Combine(AppContext.BaseDirectory, "data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorMotion");
    public static string DefaultPath => Path.Combine(DataDirectory, "settings.json");
    public static string? LoadWarning { get; private set; }
    public static Settings CreateDefault()
    {
        using var stream = typeof(Settings).Assembly.GetManifestResourceStream("CursorMotion.Assets.DefaultProfile.json")
            ?? throw new InvalidOperationException("Стартовый профиль не включён в сборку");
        return (JsonSerializer.Deserialize<Settings>(stream) ?? throw new JsonException("Пустой стартовый профиль")).Validate();
    }
    public static Settings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return CreateDefault();
        try { return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new JsonException("Empty profile")).Validate(); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LoadWarning = $"Не удалось прочитать профиль: {ex.Message}. Загружены исходные настройки.";
            return CreateDefault();
        }
    }
    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Validate(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public static Settings Preset(string name) => name switch
    {
        "Точный" => new() { Smoothing = 35, Damping = 1, Shutter = 180, Tilt = 1, Scale = 1.35 },
        "Кино" => new() { Smoothing = 180, Damping = .75, Shutter = 360, MaxSamples = 96, Scale = 2, Tilt = 7 },
        "Мгновенный" => new() { Smoothing = 0, Damping = 1, Tilt = 0 },
        "Светлый" => new() { FillColor = "#FFFFFFFF", OutlineColor = "#FF181B24", Scale = 1.8 },
        _ => new()
    };
}
