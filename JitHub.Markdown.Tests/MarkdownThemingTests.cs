using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownThemingTests
{
    [Test]
    public void Theme_presets_are_constructed()
    {
        MarkdownTheme.Light.Should().NotBeNull();
        MarkdownTheme.Dark.Should().NotBeNull();
        MarkdownTheme.HighContrast.Should().NotBeNull();

        MarkdownTheme.Light.Typography.Paragraph.FontSize.Should().BeGreaterThan(0);
        MarkdownTheme.Dark.Colors.PageBackground.A.Should().Be(255);
        MarkdownTheme.HighContrast.Typography.Link.Underline.Should().BeTrue();
    }

    [Test]
    public void StyleResolver_resolves_inline_styles_from_theme()
    {
        var theme = MarkdownTheme.Light;
        var resolver = new MarkdownStyleResolver();

        var paragraph = resolver.ResolveTextStyle(new TextInlineNode(new NodeId(1), new SourceSpan(0, 1), "x"), theme);
        paragraph.Should().Be(theme.Typography.Paragraph);

        var link = resolver.ResolveTextStyle(
            new LinkInlineNode(new NodeId(2), new SourceSpan(0, 10), "https://example.com", null, []),
            theme);
        link.Should().Be(theme.Typography.Link);
        link.Underline.Should().BeTrue();

        var code = resolver.ResolveTextStyle(new InlineCodeNode(new NodeId(3), new SourceSpan(0, 3), "x"), theme);
        code.Should().Be(theme.Typography.InlineCode);

        var emph = resolver.ResolveTextStyle(new EmphasisInlineNode(new NodeId(4), new SourceSpan(0, 5), []), theme);
        emph.Italic.Should().BeTrue();
        emph.Weight.Should().Be(theme.Typography.Paragraph.Weight);

        var strong = resolver.ResolveTextStyle(new StrongInlineNode(new NodeId(5), new SourceSpan(0, 8), []), theme);
        strong.Weight.Should().Be(FontWeight.Bold);
    }

    [Test]
    public void StyleResolver_resolves_block_styles_from_theme()
    {
        var theme = MarkdownTheme.Light;
        var resolver = new MarkdownStyleResolver();

        var code = resolver.ResolveBlockStyle(new CodeBlockNode(new NodeId(1), new SourceSpan(0, 1), "csharp", "var x=1;"), theme);
        code.Background.Should().Be(theme.Colors.CodeBlockBackground);
        code.CornerRadius.Should().Be(theme.Metrics.CornerRadius);
        code.Padding.Should().Be(theme.Metrics.BlockPadding);

        var quote = resolver.ResolveBlockStyle(new BlockQuoteBlockNode(new NodeId(2), new SourceSpan(0, 1), []), theme);
        quote.Background.Should().Be(theme.Colors.QuoteBackground);

        var hr = resolver.ResolveBlockStyle(new ThematicBreakBlockNode(new NodeId(3), new SourceSpan(0, 1)), theme);
        hr.Background.Should().Be(theme.Colors.ThematicBreak);
    }
}
