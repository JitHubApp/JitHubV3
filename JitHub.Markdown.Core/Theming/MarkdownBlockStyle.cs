namespace JitHub.Markdown;

public readonly record struct MarkdownBlockStyle(
    ColorRgba Background,
    float CornerRadius,
    float Padding,
    float SpacingAfter)
{
    public static MarkdownBlockStyle Transparent => new(ColorRgba.Transparent, CornerRadius: 0, Padding: 0, SpacingAfter: 12);
}
