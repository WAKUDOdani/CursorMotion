namespace CursorMotion;

internal sealed record CursorPack(int Id, string Name, string Description, string Accent, string Fill, string Outline, string Shadow, double Width, double Softness)
{
    public CursorAppearance Appearance(int shape) => new()
    {
        Skin = Id,
        FillColor = Fill, OutlineColor = Outline, ShadowColor = Shadow,
        OutlineWidth = Width, ShadowBlur = Softness, ShadowY = Id == 3 ? 1 : 1.6,
    };
}

internal static class CursorPacks
{
    public static readonly CursorPack[] All =
    {
        new(1, "Crystal", "Жидкое стекло · холодные блики", "#A7DDFF", "#B3B8DEFF", "#EDFFFFFF", "#35132655", 1, 3),
        new(2, "Aurora", "Жидкое стекло · переливы сирени и мяты", "#C2ADFF", "#B3BDA4FF", "#F2F3EEFF", "#3D3B2270", .8, 3.5),
        new(3, "Carbon", "Графит · точная светлая кромка", "#B8C4D6", "#FF202938", "#FFE3EEFC", "#66000000", .9, 2.2),
        new(4, "Rose", "Тёплый металл · мягкий блеск", "#F6BEAB", "#FFF0BDA8", "#FFFFF2E6", "#554B201F", .8, 3),
        new(0, "Classic", "Исходные формы · чёрный и белый", "#F1F4FA", "#FF000000", "#FFFFFFFF", "#66000000", 1.4, 2.5)
    };
    public static CursorPack Find(int id) => All.FirstOrDefault(p => p.Id == id) ?? All[^1];
    public static Settings Apply(Settings settings, int id, int? shape = null)
    {
        var pack = Find(id);
        foreach (int target in shape.HasValue ? new[] { shape.Value } : new[] { 0, 1, 2, 3 })
        {
            var old = settings.AppearanceFor(target);
            settings = settings.WithAppearance(target, pack.Appearance(target) with { Scale = old.Scale, MorphTime = old.MorphTime, IdleHide = old.IdleHide });
        }
        return settings;
    }
    public static string CurrentName(Settings s)
    {
        var pack = Find(s.AppearanceFor(0).Skin);
        return Enumerable.Range(0, 4).All(shape => MatchesMaterial(s.AppearanceFor(shape), pack.Appearance(shape))) ? pack.Name : "Свой микс";
    }
    static bool MatchesMaterial(CursorAppearance actual, CursorAppearance expected) => actual.Skin == expected.Skin
        && string.Equals(actual.FillColor, expected.FillColor, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.OutlineColor, expected.OutlineColor, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.ShadowColor, expected.ShadowColor, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(actual.OutlineWidth - expected.OutlineWidth) < .00001
        && Math.Abs(actual.ShadowBlur - expected.ShadowBlur) < .00001 && Math.Abs(actual.ShadowX - expected.ShadowX) < .00001
        // Legacy Classic used 1.5px, while pack application used 1.6px. Both are
        // the unchanged Classic material; do not relabel untouched old profiles.
        && Math.Abs(actual.ShadowY - expected.ShadowY) <= (expected.Skin == 0 ? .10001 : .00001);
}
