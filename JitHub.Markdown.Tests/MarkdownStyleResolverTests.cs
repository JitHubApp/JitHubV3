using JitHub.Markdown;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownStyleResolverTests
{
    [Test]
    public void ResolveTextStyle_applies_precedence_base_then_kind_then_modifier()
    {
        var theme = MarkdownTheme.Light;
        var r = new MarkdownStyleResolver();

        var link = new LinkInlineNode(
            Id: new NodeId(1),
            Span: default,
            Url: "https://example.com",
            Title: null,
            Inlines: ImmutableArray<InlineNode>.Empty);

        r.ResolveTextStyle(link, theme).Should().Be(theme.Typography.Link);

        var emphasis = new EmphasisInlineNode(new NodeId(2), default, ImmutableArray<InlineNode>.Empty);
        r.ResolveTextStyle(emphasis, theme).Italic.Should().BeTrue();
        r.ResolveTextStyle(emphasis, theme).FontSize.Should().Be(theme.Typography.Paragraph.FontSize);

        var strong = new StrongInlineNode(new NodeId(3), default, ImmutableArray<InlineNode>.Empty);
        r.ResolveTextStyle(strong, theme).Weight.Should().Be(FontWeight.Bold);

        var code = new InlineCodeNode(new NodeId(4), default, "x");
        r.ResolveTextStyle(code, theme).Should().Be(theme.Typography.InlineCode);
    }

    [Test]
    public void ResolveBlockStyle_matches_kind_matrix()
    {
        var theme = MarkdownTheme.Light;
        var r = new MarkdownStyleResolver();

        var code = new CodeBlockNode(new NodeId(1), default, Info: null, Code: "code");
        var codeStyle = r.ResolveBlockStyle(code, theme);
        codeStyle.Background.Should().Be(theme.Colors.CodeBlockBackground);
        codeStyle.CornerRadius.Should().Be(theme.Metrics.CornerRadius);
        codeStyle.Padding.Should().Be(theme.Metrics.BlockPadding);
        codeStyle.SpacingAfter.Should().Be(theme.Metrics.BlockSpacing);

        var quote = new BlockQuoteBlockNode(new NodeId(2), default, ImmutableArray<BlockNode>.Empty);
        var quoteStyle = r.ResolveBlockStyle(quote, theme);
        quoteStyle.Background.Should().Be(theme.Colors.QuoteBackground);
        quoteStyle.CornerRadius.Should().Be(theme.Metrics.CornerRadius);

        var list = new ListBlockNode(new NodeId(3), default, IsOrdered: false, Items: ImmutableArray<ListItemBlockNode>.Empty);
        var listStyle = r.ResolveBlockStyle(list, theme);
        listStyle.Padding.Should().Be(0);
        listStyle.CornerRadius.Should().Be(0);

        var hr = new ThematicBreakBlockNode(new NodeId(4), default);
        var hrStyle = r.ResolveBlockStyle(hr, theme);
        hrStyle.Background.Should().Be(ColorRgba.Transparent);
        hrStyle.SpacingAfter.Should().Be(theme.Metrics.BlockSpacing);
    }

    [Test]
    public void ResolveHeadingStyle_clamps_to_heading6_for_unknown_levels()
    {
        var theme = MarkdownTheme.Light;
        var r = new MarkdownStyleResolver();

        r.ResolveHeadingStyle(1, theme).Should().Be(theme.Typography.Heading1);
        r.ResolveHeadingStyle(6, theme).Should().Be(theme.Typography.Heading6);
        r.ResolveHeadingStyle(99, theme).Should().Be(theme.Typography.Heading6);
        r.ResolveHeadingStyle(0, theme).Should().Be(theme.Typography.Heading6);
    }

    [Test]
    public void ResolveTextStyleForBlock_uses_heading_level_or_paragraph()
    {
        var theme = MarkdownTheme.Light;
        var r = new MarkdownStyleResolver();

        var h = new HeadingBlockNode(new NodeId(1), default, Level: 2, Inlines: ImmutableArray<InlineNode>.Empty);
        r.ResolveTextStyleForBlock(h, theme).Should().Be(theme.Typography.Heading2);

        var p = new ParagraphBlockNode(new NodeId(2), default, Inlines: ImmutableArray<InlineNode>.Empty);
        r.ResolveTextStyleForBlock(p, theme).Should().Be(theme.Typography.Paragraph);
    }
}
