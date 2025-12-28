using JitHub.Markdown;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownHitTestingEdgeTests
{
    [Test]
    public void HitTestLine_uses_midpoint_rule_and_biases_right_on_ties()
    {
        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: default,
            Bounds: new RectF(0, 0, 20, 10),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "ab",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray.Create(0f, 10f, 20f),
            IsRightToLeft: false);

        var line = new LineLayout(0, 10, ImmutableArray.Create(run));

        MarkdownHitTester.TryHitTestLine(0, line, x: 5f, out var hit).Should().BeTrue();
        hit.TextOffset.Should().Be(1, "at exact midpoint, tie should bias to the right caret");
        hit.CaretX.Should().Be(10f);
    }

    [Test]
    public void HitTestLine_clamps_to_end_of_run_when_x_is_at_or_past_right_edge_ltr()
    {
        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: default,
            Bounds: new RectF(10, 0, 50, 10),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "hello",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray.Create(10f, 20f, 30f, 40f, 50f, 60f),
            IsRightToLeft: false);

        var line = new LineLayout(0, 10, ImmutableArray.Create(run));

        MarkdownHitTester.TryHitTestLine(0, line, x: run.Bounds.Right, out var hitAtRight).Should().BeTrue();
        hitAtRight.TextOffset.Should().Be(run.Text.Length);

        MarkdownHitTester.TryHitTestLine(0, line, x: run.Bounds.Right + 100, out var hitPastRight).Should().BeTrue();
        hitPastRight.TextOffset.Should().Be(run.Text.Length);
    }

    [Test]
    public void HitTestLine_clamps_rtl_to_start_or_end_based_on_bounds()
    {
        var rtl = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: default,
            Bounds: new RectF(10, 0, 50, 10),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "abcde",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray.Create(10f, 20f, 30f, 40f, 50f, 60f),
            IsRightToLeft: true);

        var line = new LineLayout(0, 10, ImmutableArray.Create(rtl));

        // For RTL: left edge maps to logical end, right edge maps to logical start.
        MarkdownHitTester.TryHitTestLine(0, line, x: rtl.Bounds.X, out var hitAtLeft).Should().BeTrue();
        hitAtLeft.TextOffset.Should().Be(rtl.Text.Length);

        MarkdownHitTester.TryHitTestLine(0, line, x: rtl.Bounds.Right, out var hitAtRight).Should().BeTrue();
        hitAtRight.TextOffset.Should().Be(0);

        MarkdownHitTester.TryHitTestLine(0, line, x: rtl.Bounds.X - 100, out var hitPastLeft).Should().BeTrue();
        hitPastLeft.TextOffset.Should().Be(rtl.Text.Length);

        MarkdownHitTester.TryHitTestLine(0, line, x: rtl.Bounds.Right + 100, out var hitPastRight).Should().BeTrue();
        hitPastRight.TextOffset.Should().Be(0);
    }

    [Test]
    public void HitTestLine_falls_back_to_proportional_mapping_when_glyphx_missing()
    {
        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: default,
            Bounds: new RectF(0, 0, 100, 10),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "0123456789",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: default,
            IsRightToLeft: false);

        var line = new LineLayout(0, 10, ImmutableArray.Create(run));

        MarkdownHitTester.TryHitTestLine(0, line, x: 0, out var hitLeft).Should().BeTrue();
        hitLeft.TextOffset.Should().Be(0);

        MarkdownHitTester.TryHitTestLine(0, line, x: 100, out var hitRight).Should().BeTrue();
        hitRight.TextOffset.Should().Be(run.Text.Length);

        MarkdownHitTester.TryHitTestLine(0, line, x: 50, out var hitMid).Should().BeTrue();
        hitMid.TextOffset.Should().BeInRange(4, 6);
    }

    [Test]
    public void HitTestLine_falls_back_to_proportional_mapping_and_inverts_for_rtl_when_glyphx_missing()
    {
        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: default,
            Bounds: new RectF(0, 0, 100, 10),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "0123456789",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: default,
            IsRightToLeft: true);

        var line = new LineLayout(0, 10, ImmutableArray.Create(run));

        // For RTL proportional fallback: x near left => logical end; x near right => logical start.
        MarkdownHitTester.TryHitTestLine(0, line, x: 0, out var hitLeft).Should().BeTrue();
        hitLeft.TextOffset.Should().Be(run.Text.Length);

        MarkdownHitTester.TryHitTestLine(0, line, x: 100, out var hitRight).Should().BeTrue();
        hitRight.TextOffset.Should().Be(0);
    }

    [Test]
    public void TryHitTestNearest_clamps_to_closest_line_above_or_below_in_vertical_gap()
    {
        var run1 = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: default,
            Bounds: new RectF(0, 0, 100, 10),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "top",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray.Create(0f, 33f, 66f, 100f));
        var run2 = run1 with { Text = "bottom" };

        var line1 = new LineLayout(Y: 0, Height: 10, Runs: ImmutableArray.Create(run1));
        var line2 = new LineLayout(Y: 30, Height: 10, Runs: ImmutableArray.Create(run2));

        var layout = new MarkdownLayout
        {
            Width = 200,
            Height = 100,
            Blocks = ImmutableArray.Create<BlockLayout>(
                new ParagraphLayout(
                    Id: default,
                    Span: default,
                    Bounds: new RectF(0, 0, 200, 100),
                    Style: MarkdownBlockStyle.Transparent,
                    Lines: ImmutableArray.Create(line1, line2))),
        };

        // In the gap (10..30). y=12 is closer to line1; y=28 is closer to line2.
        MarkdownHitTester.TryHitTestNearest(layout, x: 1, y: 12, out var nearTop).Should().BeTrue();
        nearTop.LineIndex.Should().Be(0);
        nearTop.Run.Text.Should().Be("top");

        MarkdownHitTester.TryHitTestNearest(layout, x: 1, y: 28, out var nearBottom).Should().BeTrue();
        nearBottom.LineIndex.Should().Be(1);
        nearBottom.Run.Text.Should().Be("bottom");
    }
}
