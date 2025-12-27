using System.Collections.Immutable;
using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownLayoutTests
{
    private sealed class TestTextMeasurer : ITextMeasurer
    {
        public TextMeasurement Measure(string text, MarkdownTextStyle style, float scale)
        {
            // Deterministic, platform-independent approximation for unit tests.
            var lineHeight = GetLineHeight(style, scale);
            var charWidth = style.FontSize * 0.6f * scale;
            var width = Math.Max(0, (text ?? string.Empty).Length * charWidth);
            return new TextMeasurement(width, lineHeight);
        }

        public float GetLineHeight(MarkdownTextStyle style, float scale)
            => Math.Max(1, style.FontSize * 1.4f * scale);
    }

    [Test]
    public void Layout_is_deterministic_for_same_input()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("# Title\n\nHello world, this is a paragraph.\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new TestTextMeasurer();

        var l1 = layoutEngine.Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);
        var l2 = layoutEngine.Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        l1.Height.Should().Be(l2.Height);
        l1.Blocks.Length.Should().Be(l2.Blocks.Length);
        for (var i = 0; i < l1.Blocks.Length; i++)
        {
            l1.Blocks[i].Bounds.Should().Be(l2.Blocks[i].Bounds);
        }
    }

    [Test]
    public void Layout_produces_no_negative_sizes()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Hello **world**\n\n> Quote\n\n```\ncode\n```\n\n---\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 480, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        layout.Width.Should().BeGreaterThanOrEqualTo(0);
        layout.Height.Should().BeGreaterThanOrEqualTo(0);

        foreach (var block in layout.Blocks)
        {
            block.Bounds.Width.Should().BeGreaterThanOrEqualTo(0);
            block.Bounds.Height.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public void Paragraph_wraps_into_multiple_lines_when_width_is_small()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("This is a long paragraph that should wrap into multiple lines when the width is small.");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 120, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var para = layout.Blocks.OfType<ParagraphLayout>().Single();
        para.Lines.Length.Should().BeGreaterThan(1);
    }

    [Test]
    public void GetVisibleBlockIndices_returns_subset_for_viewport()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(string.Join("\n\n", Enumerable.Range(1, 8).Select(i => $"Paragraph {i} with some text.")));

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var indicesTop = layout.GetVisibleBlockIndices(viewportTop: 0, viewportHeight: 60, overscan: 0);
        indicesTop.Length.Should().BeGreaterThan(0);
        indicesTop[0].Should().Be(0);

        var indicesMid = layout.GetVisibleBlockIndices(viewportTop: 200, viewportHeight: 60, overscan: 0);
        indicesMid.Length.Should().BeGreaterThan(0);
        indicesMid[0].Should().BeGreaterThan(0);

        indicesMid.Should().OnlyHaveUniqueItems();
    }

    [Test]
    public void LayoutViewport_returns_only_visible_blocks()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Paragraph {i} with some text.")));

        var layoutEngine = new MarkdownLayoutEngine();
        var full = layoutEngine.Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());
        var partial = layoutEngine.LayoutViewport(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer(), viewportTop: 0, viewportHeight: 120, overscan: 0);

        full.Blocks.Length.Should().BeGreaterThan(partial.Blocks.Length);
        partial.Blocks.Length.Should().BeGreaterThan(0);
    }

    [Test]
    public void Strikethrough_is_propagated_to_inline_runs()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("This is ~~deleted~~ text.");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var runs = layout.Blocks
            .OfType<ParagraphLayout>()
            .SelectMany(p => p.Lines)
            .SelectMany(l => l.Runs)
            .ToArray();

        runs.Any(r => r.Text.Contains("deleted", StringComparison.Ordinal) && r.IsStrikethrough).Should().BeTrue();
    }

    [Test]
    public void Lists_layout_with_markers_and_task_state()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("- one\n- [ ] todo\n- [x] done\n\n1. first\n2. second\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var lists = layout.Blocks.OfType<ListLayout>().ToArray();
        lists.Should().HaveCountGreaterThanOrEqualTo(2);

        var unordered = lists.First(l => !l.IsOrdered);
        unordered.Items.Length.Should().Be(3);
        unordered.Items[0].MarkerText.Should().Be("•");
        unordered.Items[1].MarkerText.Should().Be("☐");
        unordered.Items[2].MarkerText.Should().Be("☑");

        unordered.Items[0].Blocks.Should().NotBeEmpty();
        unordered.Items[0].Blocks[0].Bounds.X.Should().BeGreaterThan(0);

        var firstParaRuns = unordered.Items[0].Blocks
            .OfType<ParagraphLayout>()
            .SelectMany(p => p.Lines)
            .SelectMany(l => l.Runs)
            .ToArray();
        firstParaRuns.Any(r => !string.IsNullOrWhiteSpace(r.Text) && r.Bounds.X > 0).Should().BeTrue();

        var ordered = lists.First(l => l.IsOrdered);
        ordered.Items.Length.Should().Be(2);
        ordered.Items[0].MarkerText.Should().Be("1.");
        ordered.Items[1].MarkerText.Should().Be("2.");
    }

    [Test]
    public void Blockquote_offsets_children_and_runs_with_padding()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("> Quote child\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var quote = layout.Blocks.OfType<BlockQuoteLayout>().Single();
        quote.Blocks.Should().NotBeEmpty();

        var childPara = quote.Blocks.OfType<ParagraphLayout>().First();
        childPara.Lines.SelectMany(l => l.Runs).Any(r => !string.IsNullOrWhiteSpace(r.Text) && r.Bounds.X > 0).Should().BeTrue();
    }

    [Test]
    public void Tables_layout_cells_and_nested_blocks()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("| A | B |\n|---|---|\n| one | two |\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var table = layout.Blocks.OfType<TableLayout>().First();
        table.ColumnCount.Should().Be(2);
        table.Rows.Length.Should().BeGreaterThan(0);

        var firstCell = table.Rows[0].Cells[0];
        firstCell.Bounds.Width.Should().BeGreaterThan(0);
        firstCell.Blocks.Should().NotBeNull();
    }

    [Test]
    public void Images_layout_as_full_width_placeholder_runs()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("![Alt](img.png)\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: new TestTextMeasurer());

        var runs = layout.Blocks
            .OfType<ParagraphLayout>()
            .SelectMany(p => p.Lines)
            .SelectMany(l => l.Runs)
            .Where(r => r.Kind == NodeKind.Image)
            .ToArray();

        runs.Should().HaveCount(1);
        runs[0].Bounds.Width.Should().BeGreaterThan(0);
        runs[0].Bounds.Height.Should().BeApproximately(theme.Metrics.ImagePlaceholderHeight, 0.01f);
    }

    [Test]
    public void Rtl_paragraph_aligns_runs_to_content_right_edge()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("שלום עולם");

        var layoutEngine = new MarkdownLayoutEngine();
        var theme = MarkdownTheme.Light;
        var scale = 1f;

        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: scale, textMeasurer: new TestTextMeasurer());
        var para = layout.Blocks.OfType<ParagraphLayout>().Single();

        var runs = para.Lines.SelectMany(l => l.Runs).Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToArray();
        runs.Should().NotBeEmpty();

        var padding = Math.Max(0, para.Style.Padding) * scale;
        var expectedRight = layout.Width - padding;
        var maxRight = runs.Max(r => r.Bounds.Right);

        maxRight.Should().BeApproximately(expectedRight, 1.0f);
    }

    [Test]
    public void Rtl_list_places_marker_gutter_on_right()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("- שלום\n- עולם\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 400, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var list = layout.Blocks.OfType<ListLayout>().Single();
        list.Items.Length.Should().Be(2);

        // Marker should be placed on the right side for RTL.
        var item = list.Items[0];
        item.MarkerBounds.X.Should().BeGreaterThan(0);
        item.MarkerBounds.Right.Should().BeApproximately(list.Bounds.Width, 1.0f);

        // Content blocks should not be shifted right by a left-side marker gutter.
        item.Blocks.Should().NotBeEmpty();
        item.Blocks[0].Bounds.X.Should().BeApproximately(0, 0.01f);
    }

    [Test]
    public void Mixed_direction_token_is_split_into_ltr_and_rtl_runs()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("abcשלום");

        var layoutEngine = new MarkdownLayoutEngine();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: new TestTextMeasurer());

        var para = layout.Blocks.OfType<ParagraphLayout>().Single();
        var runs = para.Lines
            .SelectMany(l => l.Runs)
            .Where(r => !string.IsNullOrWhiteSpace(r.Text))
            .ToArray();

        runs.Length.Should().Be(2);
        runs[0].Text.Should().Be("abc");
        runs[0].IsRightToLeft.Should().BeFalse();
        runs[1].Text.Should().Be("שלום");
        runs[1].IsRightToLeft.Should().BeTrue();
    }
}
