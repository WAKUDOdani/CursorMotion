using System.IO;
using System.Text.Json;

namespace CursorMotion;

internal sealed record UserPreset(string Id, string Name, DateTimeOffset UpdatedAt, Settings Settings);

// One atomic file per preset: a damaged file cannot erase the rest of a library.
// Files are addressed only by generated IDs, never by user-provided names/paths.
internal sealed class UserPresets
{
    readonly string directory;
    readonly List<UserPreset> presets = new();
    public IReadOnlyList<UserPreset> Items => presets;
    public string? Warning { get; private set; }
    public UserPresets(string directory)
    {
        this.directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory)) return;
        string[] files;
        try { files = Directory.GetFiles(directory, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Warning = "Папка пресетов недоступна. Настройки курсора сохранены отдельно."; return; }
        foreach (string path in files)
        {
            try
            {
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException();
                var preset = JsonSerializer.Deserialize<UserPreset>(File.ReadAllText(path)) ?? throw new JsonException();
                if (preset.Settings is null || Path.GetFileNameWithoutExtension(path) != ValidId(preset.Id)) throw new InvalidDataException();
                presets.Add(preset with { Name = ValidName(preset.Name), Settings = preset.Settings.Validate() });
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            { Warning = "Некоторые пресеты не удалось прочитать. Остальные доступны; повреждённые файлы сохранены."; }
        }
        Sort();
    }
    static string ValidId(string id) => Guid.TryParseExact(id, "N", out var value) ? value.ToString("N") : throw new ArgumentException("Неизвестный пресет");
    static string ValidName(string name)
    {
        string result = name?.Trim() ?? "";
        if (result.Length is < 1 or > 60 || result.Any(char.IsControl)) throw new ArgumentException("Название: от 1 до 60 символов, без переносов строк");
        return result;
    }
    void Sort() => presets.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
    public UserPreset Get(string id) => presets.Find(item => item.Id == ValidId(id)) ?? throw new ArgumentException("Пресет уже удалён");
    public UserPreset Save(string name, Settings settings, string? id = null)
    {
        name = ValidName(name);
        var previous = id is null ? null : Get(id);
        if (presets.Any(item => item.Id != previous?.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Такое название уже есть. Выбери другое или обнови существующий пресет.");
        var preset = new UserPreset(previous?.Id ?? Guid.NewGuid().ToString("N"), name, DateTimeOffset.UtcNow, settings.Validate());
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, preset.Id + ".json"), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
        if (previous is not null) presets.Remove(previous);
        presets.Add(preset); Sort(); return preset;
    }
    public void Delete(string id)
    {
        var preset = Get(id);
        File.Delete(Path.Combine(directory, preset.Id + ".json"));
        presets.Remove(preset);
    }
    public void Import(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new ArgumentException("Файл пресета слишком большой");
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? throw new JsonException("Пустой профиль");
        string stem = Path.GetFileNameWithoutExtension(path).Trim();
        if (stem.Length == 0) stem = "Мой пресет";
        stem = stem[..Math.Min(stem.Length, 50)];
        string name = stem;
        for (int suffix = 2; presets.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++) name = $"{stem} ({suffix})";
        Save(name, settings);
    }
}
