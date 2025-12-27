namespace JitHub.Markdown;

public sealed class MarkdownTheme
{
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

    public required float BlockSpacing { get; init; }

    public required float BlockPadding { get; init; }
}

public sealed class MarkdownSelectionTheme
{
    public required ColorRgba SelectionFill { get; init; }

    public required ColorRgba SelectionText { get; init; }
}
