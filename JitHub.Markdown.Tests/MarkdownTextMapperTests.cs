using JitHub.Markdown;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownTextMapperTests
{
    [Test]
    public void BuildForInlines_empty_returns_empty_text_and_map()
    {
        var map = MarkdownTextMapper.BuildForInlines("", new List<InlineNode>());
        map.RenderedText.Should().BeEmpty();
        map.RenderedToSourceIndex.Should().BeEmpty();
    }

    [Test]
    public void BuildForInlines_text_inline_maps_char_by_char()
    {
        var source = "Hello";
        var inlines = new List<InlineNode>
        {
            new TextInlineNode(new NodeId(1), new SourceSpan(0, 5), "Hello"),
        };

        var map = MarkdownTextMapper.BuildForInlines(source, inlines);
        map.RenderedText.Should().Be("Hello");
        map.RenderedToSourceIndex.Should().Equal(new[] { 0, 1, 2, 3, 4 });
    }

    [Test]
    public void BuildForInlines_line_break_adds_newline_and_minus1_mapping()
    {
        var source = "A  \nB";
        var inlines = new List<InlineNode>
        {
            new TextInlineNode(new NodeId(1), new SourceSpan(0, 1), "A"),
            new LineBreakInlineNode(new NodeId(2), new SourceSpan(1, 3)),
            new TextInlineNode(new NodeId(3), new SourceSpan(4, 5), "B"),
        };

        var map = MarkdownTextMapper.BuildForInlines(source, inlines);
        map.RenderedText.Should().Be("A\nB");
        map.RenderedToSourceIndex.Should().Equal(new[] { 0, -1, 4 });
    }

    [Test]
    public void BuildForInlines_link_is_transparent_wrapper_over_children()
    {
        var source = "See [GitHub](x)";

        var link = new LinkInlineNode(
            Id: new NodeId(2),
            Span: new SourceSpan(4, 15),
            Url: "x",
            Title: null,
            Inlines: ImmutableArray.Create<InlineNode>(
                new TextInlineNode(new NodeId(3), new SourceSpan(5, 11), "GitHub")));

        var inlines = new List<InlineNode>
        {
            new TextInlineNode(new NodeId(1), new SourceSpan(0, 4), "See "),
            link,
        };

        var map = MarkdownTextMapper.BuildForInlines(source, inlines);
        map.RenderedText.Should().Be("See GitHub");
        map.RenderedToSourceIndex.Should().Equal(new[] { 0, 1, 2, 3, 5, 6, 7, 8, 9, 10 });
    }

    [Test]
    public void BuildForInlines_image_uses_alt_text_as_visible_text()
    {
        var source = "![Alt](img.png)";

        var image = new ImageInlineNode(
            Id: new NodeId(1),
            Span: new SourceSpan(0, source.Length),
            Url: "img.png",
            Title: null,
            AltText: ImmutableArray.Create<InlineNode>(
                new TextInlineNode(new NodeId(2), new SourceSpan(2, 5), "Alt")));

        var map = MarkdownTextMapper.BuildForInlines(source, new List<InlineNode> { image });
        map.RenderedText.Should().Be("Alt");
        map.RenderedToSourceIndex.Should().Equal(new[] { 2, 3, 4 });
    }

    [Test]
    public void BuildForInlines_inline_code_maps_to_inner_content_when_found()
    {
        var source = "Use `code` here";

        var code = new InlineCodeNode(
            Id: new NodeId(1),
            Span: new SourceSpan(4, 10),
            Code: "code");

        var map = MarkdownTextMapper.BuildForInlines(source, new List<InlineNode> { code });
        map.RenderedText.Should().Be("code");

        // Expect mapping to the inner content (without backticks).
        map.RenderedToSourceIndex.Should().Equal(new[] { 5, 6, 7, 8 });
    }

    [Test]
    public void BuildForInlines_inline_code_span_out_of_range_maps_to_minus1()
    {
        var source = "`code`";

        var code = new InlineCodeNode(
            Id: new NodeId(1),
            Span: new SourceSpan(0, 9999),
            Code: "code");

        var map = MarkdownTextMapper.BuildForInlines(source, new List<InlineNode> { code });
        map.RenderedText.Should().Be("code");
        map.RenderedToSourceIndex.Should().Equal(new[] { -1, -1, -1, -1 });
    }

    [Test]
    public void BuildForInlines_inline_code_content_not_found_maps_to_span_start()
    {
        var source = "`x`";

        var code = new InlineCodeNode(
            Id: new NodeId(1),
            Span: new SourceSpan(0, source.Length),
            Code: "code");

        var map = MarkdownTextMapper.BuildForInlines(source, new List<InlineNode> { code });
        map.RenderedText.Should().Be("code");
        map.RenderedToSourceIndex.Should().Equal(new[] { 0, 0, 0, 0 });
    }

    [Test]
    public void BuildForInlines_emphasis_strong_and_strike_are_transparent_wrappers()
    {
        var source = "*a* **b** ~~c~~";

        var inlines = new List<InlineNode>
        {
            new EmphasisInlineNode(new NodeId(1), new SourceSpan(0, 3), ImmutableArray.Create<InlineNode>(
                new TextInlineNode(new NodeId(2), new SourceSpan(1, 2), "a"))),
            new TextInlineNode(new NodeId(3), new SourceSpan(3, 4), " "),
            new StrongInlineNode(new NodeId(4), new SourceSpan(4, 9), ImmutableArray.Create<InlineNode>(
                new TextInlineNode(new NodeId(5), new SourceSpan(6, 7), "b"))),
            new TextInlineNode(new NodeId(6), new SourceSpan(9, 10), " "),
            new StrikethroughInlineNode(new NodeId(7), new SourceSpan(10, 15), ImmutableArray.Create<InlineNode>(
                new TextInlineNode(new NodeId(8), new SourceSpan(12, 13), "c"))),
        };

        var map = MarkdownTextMapper.BuildForInlines(source, inlines);
        map.RenderedText.Should().Be("a b c");

        map.RenderedToSourceIndex.Should().Equal(new[] { 1, 3, 6, 9, 12 });
    }
}
