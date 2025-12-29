namespace JitHub.Dashboard.Layouts;

public sealed record CardDeckLayoutSettings(
    CardDeckLayoutMode Mode,
    double ModeWidthThreshold,
    double CardMinWidth,
    double CardHeight,
    double Spacing,
    int MaxColumns,
    int DeckMaxVisibleCount,
    double DeckOffsetY,
    double DeckAngleStepDegrees,
    double DeckScaleStep)
{
    public static CardDeckLayoutSettings Default => new(
        Mode: CardDeckLayoutMode.Auto,
        ModeWidthThreshold: 900,
        CardMinWidth: 320,
        CardHeight: 180,
        Spacing: 16,
        MaxColumns: 3,
        DeckMaxVisibleCount: 5,
        DeckOffsetY: 8,
        DeckAngleStepDegrees: 1.25,
        DeckScaleStep: 0);
}
