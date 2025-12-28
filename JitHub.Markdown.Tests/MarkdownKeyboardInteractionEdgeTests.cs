using JitHub.Markdown;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownKeyboardInteractionEdgeTests
{
    [Test]
    public void Arrow_right_moves_in_visual_order_for_rtl_runs()
    {
        var run = CreateRun(NodeKind.Text, "abcde", x: 0, y: 0, width: 100, height: 10, isRightToLeft: true);
        var line = new LineLayout(0, 10, ImmutableArray.Create(run));
        var layout = CreateLayout(line);

        // RTL: logical end is visually left; pressing Right should move visually right (towards smaller logical offsets).
        var caret = CreateHit(0, 0, run, line, textOffset: run.Text.Length, caretX: MarkdownHitTester.GetCaretX(run, run.Text.Length));
        var kb = new SelectionKeyboardInteraction { Selection = new SelectionRange(caret, caret) };

        var result = kb.OnKeyCommand(layout, MarkdownKeyCommand.Right, selectionEnabled: true, shift: false);

        result.Handled.Should().BeTrue();
        result.SelectionChanged.Should().BeTrue();
        result.Selection!.Value.Active.TextOffset.Should().Be(run.Text.Length - 1);
    }

    [Test]
    public void Arrow_left_moves_in_visual_order_for_rtl_runs()
    {
        var run = CreateRun(NodeKind.Text, "abcde", x: 0, y: 0, width: 100, height: 10, isRightToLeft: true);
        var line = new LineLayout(0, 10, ImmutableArray.Create(run));
        var layout = CreateLayout(line);

        // RTL: logical start is visually right; pressing Left should move visually left (towards larger logical offsets).
        var caret = CreateHit(0, 0, run, line, textOffset: 0, caretX: MarkdownHitTester.GetCaretX(run, 0));
        var kb = new SelectionKeyboardInteraction { Selection = new SelectionRange(caret, caret) };

        var result = kb.OnKeyCommand(layout, MarkdownKeyCommand.Left, selectionEnabled: true, shift: false);

        result.Handled.Should().BeTrue();
        result.SelectionChanged.Should().BeTrue();
        result.Selection!.Value.Active.TextOffset.Should().Be(1);
    }

    [Test]
    public void Arrow_right_moves_to_next_run_when_at_end_of_current_run()
    {
        var run1 = CreateRun(NodeKind.Text, "hello", x: 0, y: 0, width: 50, height: 10);
        var run2 = CreateRun(NodeKind.Text, "world", x: 60, y: 0, width: 50, height: 10);
        var line = new LineLayout(0, 10, ImmutableArray.Create(run1, run2));
        var layout = CreateLayout(line);

        var caret = CreateHit(0, 0, run1, line, textOffset: run1.Text.Length, caretX: MarkdownHitTester.GetCaretX(run1, run1.Text.Length));
        var kb = new SelectionKeyboardInteraction { Selection = new SelectionRange(caret, caret) };

        var result = kb.OnKeyCommand(layout, MarkdownKeyCommand.Right, selectionEnabled: true, shift: false);

        result.Handled.Should().BeTrue();
        result.Selection!.Value.Active.RunIndex.Should().Be(1);
        result.Selection!.Value.Active.TextOffset.Should().Be(0);
    }

    [Test]
    public void Arrow_right_moves_to_next_non_empty_line_when_at_end_of_last_run_in_line()
    {
        var run1 = CreateRun(NodeKind.Text, "hello", x: 0, y: 0, width: 50, height: 10);
        var line1 = new LineLayout(0, 10, ImmutableArray.Create(run1));

        var empty = new LineLayout(12, 10, ImmutableArray<InlineRunLayout>.Empty);

        var run3 = CreateRun(NodeKind.Text, "next", x: 0, y: 24, width: 40, height: 10);
        var line3 = new LineLayout(24, 10, ImmutableArray.Create(run3));

        var layout = CreateLayout(line1, empty, line3);

        var caret = CreateHit(0, 0, run1, line1, textOffset: run1.Text.Length, caretX: MarkdownHitTester.GetCaretX(run1, run1.Text.Length));
        var kb = new SelectionKeyboardInteraction { Selection = new SelectionRange(caret, caret) };

        var result = kb.OnKeyCommand(layout, MarkdownKeyCommand.Right, selectionEnabled: true, shift: false);

        result.Handled.Should().BeTrue();
        result.Selection!.Value.Active.LineIndex.Should().Be(2);
        result.Selection!.Value.Active.Run.Text.Should().Be("next");
        result.Selection!.Value.Active.TextOffset.Should().Be(0);
    }

    [Test]
    public void Tab_focus_merges_multi_run_link_bounds_by_nodeid_and_url()
    {
        var linkId = new NodeId(123);
        var linkUrl = "https://example.com";

        var linkPart1 = CreateRun(NodeKind.Link, "Here", x: 0, y: 0, width: 20, height: 10, id: linkId, url: linkUrl);
        var linkPart2 = CreateRun(NodeKind.Link, "Now", x: 20, y: 0, width: 20, height: 10, id: linkId, url: linkUrl);
        var otherLink = CreateRun(NodeKind.Link, "Other", x: 60, y: 0, width: 30, height: 10, id: new NodeId(456), url: "https://example.org");

        var line = new LineLayout(0, 10, ImmutableArray.Create(linkPart1, linkPart2, otherLink));
        var layout = CreateLayout(line);

        var kb = new SelectionKeyboardInteraction();
        var tab1 = kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false);

        tab1.Handled.Should().BeTrue();
        kb.FocusedLink.Should().NotBeNull();
        kb.FocusedLink!.Value.Id.Should().Be(linkId);
        kb.FocusedLink!.Value.Url.Should().Be(linkUrl);
        kb.FocusedLink!.Value.Bounds.Should().Be(new RectF(0, 0, 40, 10));

        var tab2 = kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false);
        tab2.Handled.Should().BeTrue();
        kb.FocusedLink!.Value.Id.Should().Be(new NodeId(456));
    }

    private static MarkdownLayout CreateLayout(params LineLayout[] lines)
    {
        var p = new ParagraphLayout(
            Id: default,
            Span: default,
            Bounds: new RectF(0, 0, 200, 200),
            Style: MarkdownBlockStyle.Transparent,
            Lines: lines.ToImmutableArray());

        return new MarkdownLayout
        {
            Width = 200,
            Height = 200,
            Blocks = ImmutableArray.Create<BlockLayout>(p),
        };
    }

    private static InlineRunLayout CreateRun(NodeKind kind, string text, float x, float y, float width, float height, NodeId id = default, string? url = null, bool isRightToLeft = false)
    {
        var gx = CreateUniformGlyphX(x, width, text.Length);
        return new InlineRunLayout(
            Id: id,
            Kind: kind,
            Span: default,
            Bounds: new RectF(x, y, width, height),
            Style: kind == NodeKind.Link ? MarkdownTheme.Light.Typography.Link : MarkdownTheme.Light.Typography.Paragraph,
            Text: text,
            Url: url,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: gx,
            NodeTextOffset: 0,
            IsRightToLeft: isRightToLeft);
    }

    private static ImmutableArray<float> CreateUniformGlyphX(float x, float width, int textLength)
    {
        if (textLength <= 0)
        {
            return ImmutableArray.Create(x);
        }

        var step = width / textLength;
        var b = ImmutableArray.CreateBuilder<float>(textLength + 1);
        for (var i = 0; i <= textLength; i++)
        {
            b.Add(x + (step * i));
        }

        return b.ToImmutable();
    }

    private static MarkdownHitTestResult CreateHit(int lineIndex, int runIndex, InlineRunLayout run, LineLayout line, int textOffset, float caretX)
        => new(lineIndex, runIndex, run, line, textOffset, caretX);
}
