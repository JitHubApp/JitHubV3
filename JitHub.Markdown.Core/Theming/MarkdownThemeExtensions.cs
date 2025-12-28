namespace JitHub.Markdown;

public static class MarkdownThemeExtensions
{
    public static MarkdownTheme With(
        this MarkdownTheme theme,
        MarkdownTypography? typography = null,
        MarkdownColors? colors = null,
        MarkdownMetrics? metrics = null,
        MarkdownSelectionTheme? selection = null,
        Uri? imageBaseUri = null)
        => new()
        {
            Typography = typography ?? theme.Typography,
            Colors = colors ?? theme.Colors,
            Metrics = metrics ?? theme.Metrics,
            Selection = selection ?? theme.Selection,
            ImageBaseUri = imageBaseUri ?? theme.ImageBaseUri,
        };
}
