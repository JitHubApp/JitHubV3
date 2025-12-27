namespace JitHub.Markdown;

public sealed class MarkdownTheme
{
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MarkdownTheme()
    {
        Colors = new MarkdownColors
        {
            PageBackground = ColorRgba.White,
            InlineCodeBackground = ColorRgba.FromRgb(245, 245, 245),
            CodeBlockBackground = ColorRgba.FromRgb(245, 245, 245),
            QuoteBackground = ColorRgba.FromRgb(250, 250, 250),
            ThematicBreak = ColorRgba.FromRgb(220, 220, 220),
        };

        Metrics = new MarkdownMetrics
        {
            CornerRadius = 8,
            InlineCodeCornerRadius = 6,
            InlineCodePadding = 3,
            BlockSpacing = 12,
            BlockPadding = 12,
            ImagePlaceholderHeight = 160,
        };

        Typography = new MarkdownTypography
        {
            Paragraph = MarkdownTextStyle.Default(ColorRgba.Black),
            Heading1 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 28f, weight: FontWeight.Bold),
            Heading2 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 24f, weight: FontWeight.Bold),
            Heading3 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 20f, weight: FontWeight.SemiBold),
            Heading4 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 18f, weight: FontWeight.SemiBold),
            Heading5 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 16f, weight: FontWeight.SemiBold),
            Heading6 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 16f, weight: FontWeight.SemiBold),
            InlineCode = MarkdownTextStyle.Default(ColorRgba.Black).With(fontFamily: "Consolas", fontSize: 14f),
            Link = MarkdownTextStyle.Default(ColorRgba.FromRgb(0, 102, 204)).With(underline: true),
        };

        Selection = new MarkdownSelectionTheme
        {
            SelectionFill = ColorRgba.FromArgb(96, 0, 102, 204),
            SelectionText = ColorRgba.Black,
        };
    }

    public required MarkdownTypography Typography { get; init; }

    public required MarkdownColors Colors { get; init; }

    public required MarkdownMetrics Metrics { get; init; }

    public Uri? ImageBaseUri { get; init; }

    public required MarkdownSelectionTheme Selection { get; init; }

    public static MarkdownTheme Light => MarkdownThemePresets.Light;

    public static MarkdownTheme Dark => MarkdownThemePresets.Dark;

    public static MarkdownTheme HighContrast => MarkdownThemePresets.HighContrast;
}

public sealed class MarkdownTypography
{
    public required MarkdownTextStyle Paragraph { get; init; }

    public required MarkdownTextStyle Heading1 { get; init; }
    public required MarkdownTextStyle Heading2 { get; init; }
    public required MarkdownTextStyle Heading3 { get; init; }
    public required MarkdownTextStyle Heading4 { get; init; }
    public required MarkdownTextStyle Heading5 { get; init; }
    public required MarkdownTextStyle Heading6 { get; init; }

    public required MarkdownTextStyle InlineCode { get; init; }

    public required MarkdownTextStyle Link { get; init; }
}

public sealed class MarkdownColors
{
    public required ColorRgba PageBackground { get; init; }

    public required ColorRgba InlineCodeBackground { get; init; }

    public required ColorRgba CodeBlockBackground { get; init; }

    public required ColorRgba QuoteBackground { get; init; }

    public required ColorRgba ThematicBreak { get; init; }
}

public sealed class MarkdownMetrics
{
    public required float CornerRadius { get; init; }

    public required float InlineCodeCornerRadius { get; init; }

    public required float InlineCodePadding { get; init; }

    public required float BlockSpacing { get; init; }

    public required float BlockPadding { get; init; }

    public required float ImagePlaceholderHeight { get; init; }
}

public sealed class MarkdownSelectionTheme
{
    public required ColorRgba SelectionFill { get; init; }

    public required ColorRgba SelectionText { get; init; }
}
