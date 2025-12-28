using JitHub.Markdown;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownSelectionGeometryTests
{
    [Test]
    public void Build_single_line_selection_emits_rect_and_per_run_segments_clipped_to_selection()
    {
        var lineY = 10f;
        var lineHeight = 20f;

        var run1 = CreateRun(NodeKind.Text, "Hello", x: 0, width: 50, height: lineHeight);
        var run2 = CreateRun(NodeKind.Link, "Link", x: 50, width: 30, height: lineHeight, url: "https://example.com");
        var run3 = CreateRun(NodeKind.Text, "world", x: 80, width: 50, height: lineHeight);

        var line = new LineLayout(lineY, lineHeight, ImmutableArray.Create(run1, run2, run3));
        var layout = CreateLayout(width: 200, height: 100, line);

        var start = CreateHit(lineIndex: 0, runIndex: 0, run1, line, textOffset: 2, caretX: 20f);
        var end = CreateHit(lineIndex: 0, runIndex: 2, run3, line, textOffset: 3, caretX: 110f);

        var geo = SelectionGeometryBuilder.Build(layout, new SelectionRange(start, end));

        geo.Rects.Should().HaveCount(1);
        geo.Rects[0].X.Should().Be(20f);
        geo.Rects[0].Y.Should().Be(lineY);
        geo.Rects[0].Width.Should().Be(90f);
        geo.Rects[0].Height.Should().Be(lineHeight);

        geo.Segments.Should().HaveCount(3);
        geo.Segments[0].Kind.Should().Be(NodeKind.Text);
        geo.Segments[0].Rect.Should().Be(new RectF(20, lineY, 30, lineHeight));

        geo.Segments[1].Kind.Should().Be(NodeKind.Link);
        geo.Segments[1].Rect.Should().Be(new RectF(50, lineY, 30, lineHeight));

        geo.Segments[2].Kind.Should().Be(NodeKind.Text);
        geo.Segments[2].Rect.Should().Be(new RectF(80, lineY, 30, lineHeight));
    }

    [Test]
    public void Build_collapsed_selection_emits_1px_caret_rect_and_no_segments()
    {
        var lineY = 0f;
        var lineHeight = 16f;

        var run = CreateRun(NodeKind.Text, "hello", x: 10, width: 50, height: lineHeight);
        var line = new LineLayout(lineY, lineHeight, ImmutableArray.Create(run));
        var layout = CreateLayout(width: 200, height: 50, line);

        var caret = CreateHit(lineIndex: 0, runIndex: 0, run, line, textOffset: 2, caretX: 25f);
        var geo = SelectionGeometryBuilder.Build(layout, new SelectionRange(caret, caret));

        geo.Rects.Should().HaveCount(1);
        geo.Rects[0].Should().Be(new RectF(25, lineY, 1f, lineHeight));
        geo.Segments.Should().BeEmpty();
    }

    [Test]
    public void Build_multi_line_selection_uses_line_extents_for_middle_and_end_lines()
    {
        var line1 = new LineLayout(0, 10, ImmutableArray.Create(
            CreateRun(NodeKind.Text, "AAAA", x: 0, width: 100, height: 10)));

        var line2 = new LineLayout(12, 10, ImmutableArray.Create(
            CreateRun(NodeKind.Text, "BBBB", x: 10, width: 110, height: 10)));

        var layout = CreateLayout(width: 200, height: 80, line1, line2);

        var startRun = line1.Runs[0];
        var endRun = line2.Runs[0];

        var start = CreateHit(lineIndex: 0, runIndex: 0, startRun, line1, textOffset: 1, caretX: 30f);
        var end = CreateHit(lineIndex: 1, runIndex: 0, endRun, line2, textOffset: 1, caretX: 50f);

        var geo = SelectionGeometryBuilder.Build(layout, new SelectionRange(start, end));

        geo.Rects.Should().HaveCount(2);
        geo.Rects[0].Should().Be(new RectF(30, line1.Y, 70f, line1.Height));
        geo.Rects[1].Should().Be(new RectF(10, line2.Y, 40f, line2.Height));
    }

    [Test]
    public void Build_swaps_x1_x2_when_rtl_caret_positions_are_reversed()
    {
        var lineY = 0f;
        var lineHeight = 16f;

        var rtlRun = CreateRun(NodeKind.Text, "مرحبا", x: 0, width: 100, height: lineHeight, isRightToLeft: true);
        var line = new LineLayout(lineY, lineHeight, ImmutableArray.Create(rtlRun));
        var layout = CreateLayout(width: 200, height: 40, line);

        var startCaretX = MarkdownHitTester.GetCaretX(rtlRun, textOffset: 1);
        var endCaretX = MarkdownHitTester.GetCaretX(rtlRun, textOffset: 4);
        startCaretX.Should().BeGreaterThan(endCaretX, "RTL caret X decreases as logical offset increases");

        var start = CreateHit(0, 0, rtlRun, line, textOffset: 1, caretX: startCaretX);
        var end = CreateHit(0, 0, rtlRun, line, textOffset: 4, caretX: endCaretX);

        var geo = SelectionGeometryBuilder.Build(layout, new SelectionRange(start, end));

        geo.Rects.Should().HaveCount(1);
        geo.Rects[0].X.Should().Be(endCaretX);
        geo.Rects[0].Width.Should().Be(startCaretX - endCaretX);
    }

    [Test]
    public void Build_skips_empty_lines_but_preserves_line_indices_for_range()
    {
        var line1 = new LineLayout(0, 10, ImmutableArray.Create(CreateRun(NodeKind.Text, "A", x: 0, width: 40, height: 10)));
        var emptyLine = new LineLayout(12, 10, ImmutableArray<InlineRunLayout>.Empty);
        var line3 = new LineLayout(24, 10, ImmutableArray.Create(CreateRun(NodeKind.Text, "B", x: 0, width: 40, height: 10)));

        var layout = CreateLayout(width: 200, height: 80, line1, emptyLine, line3);

        var startRun = line1.Runs[0];
        var endRun = line3.Runs[0];

        var start = CreateHit(lineIndex: 0, runIndex: 0, startRun, line1, textOffset: 0, caretX: 0f);
        var end = CreateHit(lineIndex: 2, runIndex: 0, endRun, line3, textOffset: 1, caretX: 40f);

        var geo = SelectionGeometryBuilder.Build(layout, new SelectionRange(start, end));

        geo.Rects.Should().HaveCount(2);
        geo.Rects[0].Y.Should().Be(line1.Y);
        geo.Rects[1].Y.Should().Be(line3.Y);
    }

    private static MarkdownLayout CreateLayout(float width, float height, params LineLayout[] lines)
    {
        var p = new ParagraphLayout(
            Id: default,
            Span: default,
            Bounds: new RectF(0, 0, width, height),
            Style: MarkdownBlockStyle.Transparent,
            Lines: lines.ToImmutableArray());

        return new MarkdownLayout
        {
            Width = width,
            Height = height,
            Blocks = ImmutableArray.Create<BlockLayout>(p),
        };
    }

    private static InlineRunLayout CreateRun(NodeKind kind, string text, float x, float width, float height, string? url = null, bool isCodeBlockLine = false, bool isRightToLeft = false)
    {
        // Create a simple evenly spaced caret boundary array so hit-testing and selection are deterministic.
        var gx = CreateUniformGlyphX(x, width, text.Length);

        return new InlineRunLayout(
            Id: default,
            Kind: kind,
            Span: default,
            Bounds: new RectF(x, 0, width, height),
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: text,
            Url: url,
            IsStrikethrough: false,
            IsCodeBlockLine: isCodeBlockLine,
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
