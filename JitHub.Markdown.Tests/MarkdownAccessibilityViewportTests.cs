using JitHub.Markdown;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownAccessibilityViewportTests
{
    [Test]
    public void Build_filters_blocks_by_viewport_intersection()
    {
        var p1 = CreateParagraphBlock(new NodeId(1), y: 0, height: 50, text: "First");
        var p2 = CreateParagraphBlock(new NodeId(2), y: 150, height: 50, text: "Second");

        var layout = new MarkdownLayout
        {
            Width = 200,
            Height = 220,
            Blocks = ImmutableArray.Create<BlockLayout>(p1, p2),
        };

        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: 60);
        tree.Root.Children.Should().HaveCount(1);
        tree.Root.Children[0].Id.Should().Be(new NodeId(1));
    }

    [Test]
    public void Build_includes_more_blocks_when_overscan_expands_view()
    {
        var p1 = CreateParagraphBlock(new NodeId(1), y: 0, height: 50, text: "First");
        var p2 = CreateParagraphBlock(new NodeId(2), y: 150, height: 50, text: "Second");

        var layout = new MarkdownLayout
        {
            Width = 200,
            Height = 220,
            Blocks = ImmutableArray.Create<BlockLayout>(p1, p2),
        };

        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: 60, overscan: 200);
        tree.Root.Children.Select(c => c.Id).Should().Contain(new[] { new NodeId(1), new NodeId(2) });
    }

    [Test]
    public void Build_creates_inline_link_children_for_paragraphs()
    {
        var paraId = new NodeId(10);
        var linkId = new NodeId(11);

        var textRun = CreateRun(NodeKind.Text, "See ", id: new NodeId(12), x: 0, y: 0, width: 30, height: 16);
        var linkRun = CreateRun(NodeKind.Link, "GitHub", id: linkId, x: 30, y: 0, width: 40, height: 16, url: "https://github.com");
        var endRun = CreateRun(NodeKind.Text, " now", id: new NodeId(13), x: 70, y: 0, width: 30, height: 16);

        var line = new LineLayout(0, 16, ImmutableArray.Create(textRun, linkRun, endRun));
        var para = new ParagraphLayout(
            Id: paraId,
            Span: default,
            Bounds: new RectF(0, 0, 200, 40),
            Style: MarkdownBlockStyle.Transparent,
            Lines: ImmutableArray.Create(line));

        var layout = new MarkdownLayout
        {
            Width = 200,
            Height = 40,
            Blocks = ImmutableArray.Create<BlockLayout>(para),
        };

        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: 40);
        tree.Root.Children.Should().HaveCount(1);

        var paragraphNode = tree.Root.Children[0];
        paragraphNode.Role.Should().Be(MarkdownAccessibilityRole.Paragraph);
        paragraphNode.Name.Should().Be("See GitHub now");

        paragraphNode.Children.Should().ContainSingle(c => c.Role == MarkdownAccessibilityRole.Link);
        var linkNode = paragraphNode.Children.Single(c => c.Role == MarkdownAccessibilityRole.Link);
        linkNode.Id.Should().Be(linkId);
        linkNode.Name.Should().Be("GitHub");
        linkNode.Url.Should().Be("https://github.com");
        linkNode.Bounds.Should().Be(linkRun.Bounds);
    }

    [Test]
    public void Build_filters_list_items_by_viewport()
    {
        var item1 = CreateListItem(new NodeId(101), y: 0, height: 40, marker: "-");
        var item2 = CreateListItem(new NodeId(102), y: 120, height: 40, marker: "-");

        var list = new ListLayout(
            Id: new NodeId(100),
            Span: default,
            Bounds: new RectF(0, 0, 200, 200),
            Style: MarkdownBlockStyle.Transparent,
            IsOrdered: false,
            Items: ImmutableArray.Create(item1, item2));

        var layout = new MarkdownLayout
        {
            Width = 200,
            Height = 200,
            Blocks = ImmutableArray.Create<BlockLayout>(list),
        };

        var tree = MarkdownAccessibilityTreeBuilder.Build(layout, viewportTop: 0, viewportHeight: 80);
        tree.Root.Children.Should().ContainSingle(c => c.Role == MarkdownAccessibilityRole.List);

        var listNode = tree.Root.Children.Single(c => c.Role == MarkdownAccessibilityRole.List);
        listNode.Children.Should().HaveCount(1);
        listNode.Children[0].Id.Should().Be(new NodeId(101));
    }

    private static ParagraphLayout CreateParagraphBlock(NodeId id, float y, float height, string text)
    {
        var run = CreateRun(NodeKind.Text, text, id: new NodeId(id.Value + 1000), x: 0, y: y, width: 100, height: 16);
        var line = new LineLayout(y, 16, ImmutableArray.Create(run));

        return new ParagraphLayout(
            Id: id,
            Span: default,
            Bounds: new RectF(0, y, 200, height),
            Style: MarkdownBlockStyle.Transparent,
            Lines: ImmutableArray.Create(line));
    }

    private static ListItemLayout CreateListItem(NodeId id, float y, float height, string marker)
    {
        var para = CreateParagraphBlock(new NodeId(id.Value + 10), y: y, height: height, text: "Item");

        return new ListItemLayout(
            Id: id,
            Span: default,
            Bounds: new RectF(0, y, 200, height),
            Style: MarkdownBlockStyle.Transparent,
            MarkerText: marker,
            MarkerBounds: new RectF(0, y, 10, 10),
            Blocks: ImmutableArray.Create<BlockLayout>(para));
    }

    private static InlineRunLayout CreateRun(NodeKind kind, string text, NodeId id, float x, float y, float width, float height, string? url = null)
        => new(
            Id: id,
            Kind: kind,
            Span: default,
            Bounds: new RectF(x, y, width, height),
            Style: kind == NodeKind.Link ? MarkdownTheme.Light.Typography.Link : MarkdownTheme.Light.Typography.Paragraph,
            Text: text,
            Url: url,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: default,
            NodeTextOffset: 0,
            IsRightToLeft: false);
}
