namespace JitHub.Markdown;

public readonly record struct MarkdownTextStyle(
    string? FontFamily,
    float FontSize,
    FontWeight Weight,
    bool Italic,
    bool Underline,
    ColorRgba Foreground)
{
    public static MarkdownTextStyle Default(ColorRgba foreground)
        => new(FontFamily: null, FontSize: 16f, Weight: FontWeight.Normal, Italic: false, Underline: false, Foreground: foreground);

    public MarkdownTextStyle With(
        string? fontFamily = null,
        float? fontSize = null,
        FontWeight? weight = null,
        bool? italic = null,
        bool? underline = null,
        ColorRgba? foreground = null)
        => new(
            FontFamily: fontFamily ?? FontFamily,
            FontSize: fontSize ?? FontSize,
            Weight: weight ?? Weight,
            Italic: italic ?? Italic,
            Underline: underline ?? Underline,
            Foreground: foreground ?? Foreground);
}

public enum FontWeight
{
    Normal = 0,
    SemiBold = 1,
    Bold = 2,
}
