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
}
