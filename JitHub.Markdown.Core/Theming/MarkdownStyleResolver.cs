namespace JitHub.Markdown;

public sealed class MarkdownStyleResolver : IMarkdownStyleResolver
{
    public MarkdownTextStyle ResolveTextStyle(InlineNode inline, MarkdownTheme theme)
    {
        // Precedence: base element style (paragraph) → inline modifier.
        var style = theme.Typography.Paragraph;

        style = inline.Kind switch
        {
            NodeKind.Link => theme.Typography.Link,
            NodeKind.InlineCode => theme.Typography.InlineCode,
            _ => style
        };

        style = inline.Kind switch
        {
            NodeKind.Emphasis => style.With(italic: true),
            NodeKind.Strong => style.With(weight: FontWeight.Bold),
            NodeKind.Strikethrough => style, // decoration handled by renderer later
            _ => style
        };

        return style;
    }

    public MarkdownBlockStyle ResolveBlockStyle(BlockNode block, MarkdownTheme theme)
    {
        var baseStyle = MarkdownBlockStyle.Transparent;

        return block.Kind switch
        {
            NodeKind.Table => baseStyle with
            {
                Background = ColorRgba.Transparent,
                CornerRadius = 0,
                Padding = theme.Metrics.BlockPadding,
                SpacingAfter = theme.Metrics.BlockSpacing,
            },

            NodeKind.List => baseStyle with
            {
                Background = ColorRgba.Transparent,
                CornerRadius = 0,
                Padding = 0,
                SpacingAfter = theme.Metrics.BlockSpacing,
            },

            NodeKind.ListItem => baseStyle with
            {
                Background = ColorRgba.Transparent,
                CornerRadius = 0,
                Padding = 0,
                SpacingAfter = 0,
            },

            NodeKind.CodeBlock => baseStyle with
            {
                Background = theme.Colors.CodeBlockBackground,
                CornerRadius = theme.Metrics.CornerRadius,
                Padding = theme.Metrics.BlockPadding,
                SpacingAfter = theme.Metrics.BlockSpacing,
            },

            NodeKind.BlockQuote => baseStyle with
            {
                Background = theme.Colors.QuoteBackground,
                CornerRadius = theme.Metrics.CornerRadius,
                Padding = theme.Metrics.BlockPadding,
                SpacingAfter = theme.Metrics.BlockSpacing,
            },

            NodeKind.ThematicBreak => baseStyle with
            {
                Background = ColorRgba.Transparent,
                CornerRadius = 0,
                Padding = 0,
                SpacingAfter = theme.Metrics.BlockSpacing,
            },

            _ => baseStyle with
            {
                Background = ColorRgba.Transparent,
                CornerRadius = 0,
                Padding = 0,
                SpacingAfter = theme.Metrics.BlockSpacing,
            }
        };
    }

    public MarkdownTextStyle ResolveHeadingStyle(int level, MarkdownTheme theme)
        => level switch
        {
            1 => theme.Typography.Heading1,
            2 => theme.Typography.Heading2,
            3 => theme.Typography.Heading3,
            4 => theme.Typography.Heading4,
            5 => theme.Typography.Heading5,
            _ => theme.Typography.Heading6,
        };

    public MarkdownTextStyle ResolveTextStyleForBlock(BlockNode block, MarkdownTheme theme)
    {
        if (block is HeadingBlockNode heading)
        {
            return ResolveHeadingStyle(heading.Level, theme);
        }

        return theme.Typography.Paragraph;
    }
}
