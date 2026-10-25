namespace CursorMotion;

public sealed record CursorAppearance
{
    public int Skin { get; init; }
    public double Scale { get; init; } = 1.6;
    public bool ExactSize { get; init; }
    public string FillColor { get; init; } = "#FF000000";
    public string OutlineColor { get; init; } = "#FFFFFFFF";
    public string ShadowColor { get; init; } = "#66000000";
    public double OutlineWidth { get; init; } = 1.4;
    public double ShadowBlur { get; init; } = 2.5;
    public double ShadowX { get; init; }
    public double ShadowY { get; init; } = 1.5;
    public double MorphTime { get; init; } = 140;
    public double IdleHide { get; init; }

    public CursorAppearance Validate() => this with
    {
        Skin = Math.Clamp(Skin, 0, 4),
        Scale = Settings.Limit(Scale, .5, Settings.MaximumCursorScale, 1.6),
        FillColor = Settings.ValidColor(FillColor, "#FF000000"),
        OutlineColor = Settings.ValidColor(OutlineColor, "#FFFFFFFF"),
        ShadowColor = Settings.ValidColor(ShadowColor, "#66000000"),
        OutlineWidth = Settings.Limit(OutlineWidth, 0, 4, 1.4),
        ShadowBlur = Settings.Limit(ShadowBlur, 0, 10, 2.5),
        ShadowX = Settings.Limit(ShadowX, -10, 10, 0), ShadowY = Settings.Limit(ShadowY, -10, 10, 1.5),
        MorphTime = Settings.Limit(MorphTime, 0, 500, 140), IdleHide = Settings.Limit(IdleHide, 0, 30, 0)
    };
}
