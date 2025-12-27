namespace JitHub.Markdown;

public static class MarkdownThemePresets
{
    public static MarkdownTheme Light { get; } = new()
    {
        Colors = new MarkdownColors
        {
            PageBackground = ColorRgba.White,
            InlineCodeBackground = ColorRgba.FromRgb(245, 245, 245),
            CodeBlockBackground = ColorRgba.FromRgb(245, 245, 245),
            QuoteBackground = ColorRgba.FromRgb(250, 250, 250),
            ThematicBreak = ColorRgba.FromRgb(220, 220, 220),
        },
        Metrics = new MarkdownMetrics
        {
            CornerRadius = 8,
            InlineCodeCornerRadius = 6,
            InlineCodePadding = 3,
            BlockSpacing = 12,
            BlockPadding = 12,
            ImagePlaceholderHeight = 160,
        },
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
        },
        Selection = new MarkdownSelectionTheme
        {
            SelectionFill = ColorRgba.FromArgb(96, 0, 102, 204),
            SelectionText = ColorRgba.Black,
        }
    };

    public static MarkdownTheme Dark { get; } = new()
    {
        Colors = new MarkdownColors
        {
            PageBackground = ColorRgba.FromRgb(20, 20, 20),
            InlineCodeBackground = ColorRgba.FromRgb(35, 35, 35),
            CodeBlockBackground = ColorRgba.FromRgb(35, 35, 35),
            QuoteBackground = ColorRgba.FromRgb(30, 30, 30),
            ThematicBreak = ColorRgba.FromRgb(70, 70, 70),
        },
        Metrics = new MarkdownMetrics
        {
            CornerRadius = 8,
            InlineCodeCornerRadius = 6,
            InlineCodePadding = 3,
            BlockSpacing = 12,
            BlockPadding = 12,
            ImagePlaceholderHeight = 160,
        },
        Typography = new MarkdownTypography
        {
            Paragraph = MarkdownTextStyle.Default(ColorRgba.FromRgb(235, 235, 235)),
            Heading1 = MarkdownTextStyle.Default(ColorRgba.FromRgb(245, 245, 245)).With(fontSize: 28f, weight: FontWeight.Bold),
            Heading2 = MarkdownTextStyle.Default(ColorRgba.FromRgb(245, 245, 245)).With(fontSize: 24f, weight: FontWeight.Bold),
            Heading3 = MarkdownTextStyle.Default(ColorRgba.FromRgb(245, 245, 245)).With(fontSize: 20f, weight: FontWeight.SemiBold),
            Heading4 = MarkdownTextStyle.Default(ColorRgba.FromRgb(245, 245, 245)).With(fontSize: 18f, weight: FontWeight.SemiBold),
            Heading5 = MarkdownTextStyle.Default(ColorRgba.FromRgb(245, 245, 245)).With(fontSize: 16f, weight: FontWeight.SemiBold),
            Heading6 = MarkdownTextStyle.Default(ColorRgba.FromRgb(245, 245, 245)).With(fontSize: 16f, weight: FontWeight.SemiBold),
            InlineCode = MarkdownTextStyle.Default(ColorRgba.FromRgb(235, 235, 235)).With(fontFamily: "Consolas", fontSize: 14f),
            Link = MarkdownTextStyle.Default(ColorRgba.FromRgb(110, 170, 255)).With(underline: true),
        },
        Selection = new MarkdownSelectionTheme
        {
            SelectionFill = ColorRgba.FromArgb(96, 110, 170, 255),
            SelectionText = ColorRgba.White,
        }
    };

    public static MarkdownTheme HighContrast { get; } = new()
    {
        Colors = new MarkdownColors
        {
            PageBackground = ColorRgba.Black,
            InlineCodeBackground = ColorRgba.Black,
            CodeBlockBackground = ColorRgba.Black,
            QuoteBackground = ColorRgba.Black,
            ThematicBreak = ColorRgba.White,
        },
        Metrics = new MarkdownMetrics
        {
            CornerRadius = 0,
            InlineCodeCornerRadius = 0,
            InlineCodePadding = 0,
            BlockSpacing = 12,
            BlockPadding = 12,
            ImagePlaceholderHeight = 160,
        },
        Typography = new MarkdownTypography
        {
            Paragraph = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 18f, weight: FontWeight.Bold),
            Heading1 = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 30f, weight: FontWeight.Bold),
            Heading2 = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 26f, weight: FontWeight.Bold),
            Heading3 = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 22f, weight: FontWeight.Bold),
            Heading4 = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 20f, weight: FontWeight.Bold),
            Heading5 = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 18f, weight: FontWeight.Bold),
            Heading6 = MarkdownTextStyle.Default(ColorRgba.White).With(fontSize: 18f, weight: FontWeight.Bold),
            InlineCode = MarkdownTextStyle.Default(ColorRgba.White).With(fontFamily: "Consolas", fontSize: 16f, weight: FontWeight.Bold),
            Link = MarkdownTextStyle.Default(ColorRgba.White).With(underline: true),
        },
        Selection = new MarkdownSelectionTheme
        {
            SelectionFill = ColorRgba.White,
            SelectionText = ColorRgba.Black,
        }
    };
}
