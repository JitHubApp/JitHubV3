namespace JitHub.Markdown;

public sealed class MarkdownThemeOverrides
{
    public MarkdownTypographyOverrides? Typography { get; init; }
    public MarkdownColorsOverrides? Colors { get; init; }
    public MarkdownMetricsOverrides? Metrics { get; init; }
    public MarkdownSelectionOverrides? Selection { get; init; }

    public static MarkdownTheme Apply(MarkdownTheme baseTheme, MarkdownThemeOverrides? overrides)
    {
        if (overrides is null)
        {
            return baseTheme;
        }

        var typography = overrides.Typography is null ? baseTheme.Typography : ApplyTypography(baseTheme.Typography, overrides.Typography);
        var colors = overrides.Colors is null ? baseTheme.Colors : ApplyColors(baseTheme.Colors, overrides.Colors);
        var metrics = overrides.Metrics is null ? baseTheme.Metrics : ApplyMetrics(baseTheme.Metrics, overrides.Metrics);
        var selection = overrides.Selection is null ? baseTheme.Selection : ApplySelection(baseTheme.Selection, overrides.Selection);

        return baseTheme.With(typography: typography, colors: colors, metrics: metrics, selection: selection);
    }

    private static MarkdownTypography ApplyTypography(MarkdownTypography baseTypography, MarkdownTypographyOverrides overrides)
        => new()
        {
            Paragraph = overrides.Paragraph ?? baseTypography.Paragraph,
            Heading1 = overrides.Heading1 ?? baseTypography.Heading1,
            Heading2 = overrides.Heading2 ?? baseTypography.Heading2,
            Heading3 = overrides.Heading3 ?? baseTypography.Heading3,
            Heading4 = overrides.Heading4 ?? baseTypography.Heading4,
            Heading5 = overrides.Heading5 ?? baseTypography.Heading5,
            Heading6 = overrides.Heading6 ?? baseTypography.Heading6,
            InlineCode = overrides.InlineCode ?? baseTypography.InlineCode,
            Link = overrides.Link ?? baseTypography.Link,
        };

    private static MarkdownColors ApplyColors(MarkdownColors baseColors, MarkdownColorsOverrides overrides)
        => new()
        {
            PageBackground = overrides.PageBackground ?? baseColors.PageBackground,
            InlineCodeBackground = overrides.InlineCodeBackground ?? baseColors.InlineCodeBackground,
            CodeBlockBackground = overrides.CodeBlockBackground ?? baseColors.CodeBlockBackground,
            QuoteBackground = overrides.QuoteBackground ?? baseColors.QuoteBackground,
            ThematicBreak = overrides.ThematicBreak ?? baseColors.ThematicBreak,
        };

    private static MarkdownMetrics ApplyMetrics(MarkdownMetrics baseMetrics, MarkdownMetricsOverrides overrides)
        => new()
        {
            CornerRadius = overrides.CornerRadius ?? baseMetrics.CornerRadius,
            InlineCodeCornerRadius = overrides.InlineCodeCornerRadius ?? baseMetrics.InlineCodeCornerRadius,
            InlineCodePadding = overrides.InlineCodePadding ?? baseMetrics.InlineCodePadding,
            BlockSpacing = overrides.BlockSpacing ?? baseMetrics.BlockSpacing,
            BlockPadding = overrides.BlockPadding ?? baseMetrics.BlockPadding,
            ImagePlaceholderHeight = overrides.ImagePlaceholderHeight ?? baseMetrics.ImagePlaceholderHeight,
        };

    private static MarkdownSelectionTheme ApplySelection(MarkdownSelectionTheme baseSelection, MarkdownSelectionOverrides overrides)
        => new()
        {
            SelectionFill = overrides.SelectionFill ?? baseSelection.SelectionFill,
            SelectionText = overrides.SelectionText ?? baseSelection.SelectionText,
        };
}

public sealed class MarkdownTypographyOverrides
{
    public MarkdownTextStyle? Paragraph { get; init; }
    public MarkdownTextStyle? Heading1 { get; init; }
    public MarkdownTextStyle? Heading2 { get; init; }
    public MarkdownTextStyle? Heading3 { get; init; }
    public MarkdownTextStyle? Heading4 { get; init; }
    public MarkdownTextStyle? Heading5 { get; init; }
    public MarkdownTextStyle? Heading6 { get; init; }
    public MarkdownTextStyle? InlineCode { get; init; }
    public MarkdownTextStyle? Link { get; init; }
}

public sealed class MarkdownColorsOverrides
{
    public ColorRgba? PageBackground { get; init; }
    public ColorRgba? InlineCodeBackground { get; init; }
    public ColorRgba? CodeBlockBackground { get; init; }
    public ColorRgba? QuoteBackground { get; init; }
    public ColorRgba? ThematicBreak { get; init; }
}

public sealed class MarkdownMetricsOverrides
{
    public float? CornerRadius { get; init; }
    public float? InlineCodeCornerRadius { get; init; }
    public float? InlineCodePadding { get; init; }
    public float? BlockSpacing { get; init; }
    public float? BlockPadding { get; init; }
    public float? ImagePlaceholderHeight { get; init; }
}

public sealed class MarkdownSelectionOverrides
{
    public ColorRgba? SelectionFill { get; init; }
    public ColorRgba? SelectionText { get; init; }
}
