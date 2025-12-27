using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownSelectionTests
{
    [Test]
    public void Hit_test_returns_correct_run_and_offset()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Hello world");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var helloRun = line.Runs.First(r => r.Text == "Hello");

        helloRun.GlyphX.IsDefault.Should().BeFalse("glyph boundaries should be generated during layout");
        helloRun.GlyphX.Length.Should().Be(helloRun.Text.Length + 1);

        var x = (helloRun.GlyphX[0] + helloRun.GlyphX[helloRun.GlyphX.Length - 1]) / 2f;
        var y = line.Y + (line.Height / 2f);

        MarkdownHitTester.TryHitTest(layout, x, y, out var hit).Should().BeTrue();
        hit.Run.Text.Should().Be("Hello");
        hit.TextOffset.Should().BeGreaterThan(0);
        hit.TextOffset.Should().BeLessThan(helloRun.Text.Length);
    }

    [Test]
    public void Hit_test_nearest_succeeds_in_vertical_gaps()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Hello world");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var helloRun = line.Runs.First(r => r.Text == "Hello");

        var x = (helloRun.Bounds.X + helloRun.Bounds.Right) / 2f;
        var yBetweenLines = line.Y - 1f;

        MarkdownHitTester.TryHitTest(layout, x, yBetweenLines, out _).Should().BeFalse("TryHitTest requires Y to fall inside a line band");

        MarkdownHitTester.TryHitTestNearest(layout, x, yBetweenLines, out var hit).Should().BeTrue("nearest hit-test should clamp to the closest line");
        hit.Run.Text.Should().Be("Hello");
    }

    [Test]
    public void Selection_geometry_spans_wrapped_lines_without_vertical_gaps()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Hello world again and again and again");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 140, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var lines = layout.Blocks.OfType<ParagraphLayout>().Single().Lines;
        lines.Length.Should().BeGreaterThanOrEqualTo(2);

        var firstLine = lines[0];
        var lastLine = lines[lines.Length - 1];

        var firstRun = firstLine.Runs.First(r => r.Text.Length > 0 && !string.IsNullOrWhiteSpace(r.Text));
        var lastRun = lastLine.Runs.Last(r => r.Text.Length > 0 && !string.IsNullOrWhiteSpace(r.Text));

        var start = new MarkdownHitTestResult(
            LineIndex: 0,
            RunIndex: 0,
            Run: firstRun,
            Line: firstLine,
            TextOffset: 1,
            CaretX: MarkdownHitTester.GetCaretX(firstRun, 1));

        var end = new MarkdownHitTestResult(
            LineIndex: lines.Length - 1,
            RunIndex: lastLine.Runs.Length - 1,
            Run: lastRun,
            Line: lastLine,
            TextOffset: Math.Max(0, lastRun.Text.Length - 1),
            CaretX: MarkdownHitTester.GetCaretX(lastRun, Math.Max(0, lastRun.Text.Length - 1)));

        var geometry = SelectionGeometryBuilder.Build(layout, new SelectionRange(start, end));
        geometry.Rects.Length.Should().BeGreaterThanOrEqualTo(2);

        // The layout engine increments lineY by lineHeight, so selected rects should stack with no gaps.
        for (var i = 0; i < geometry.Rects.Length - 1; i++)
        {
            var a = geometry.Rects[i];
            var b = geometry.Rects[i + 1];
            b.Y.Should().BeApproximately(a.Bottom, 0.01f);
        }
    }

    [Test]
    public void Source_mapping_for_emphasis_maps_inside_markers()
    {
        var markdown = "Hello **world**.";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var runIndex = line.Runs.ToList().FindIndex(r => r.Text == "world");
        runIndex.Should().BeGreaterThanOrEqualTo(0);

        var run = line.Runs[runIndex];

        var anchor = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 0, CaretX: MarkdownHitTester.GetCaretX(run, 0));
        var active = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 5, CaretX: MarkdownHitTester.GetCaretX(run, 5));
        var sel = new SelectionRange(anchor, active);

        SelectionSourceMapper.TryMapToSource(markdown, doc, sel, out var sourceSel).Should().BeTrue();
        sourceSel.Slice(markdown).Should().Be("world");
    }

    [Test]
    public void Source_mapping_for_inline_code_maps_to_inner_code_only()
    {
        var markdown = "Inline `code` here.";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var runIndex = line.Runs.ToList().FindIndex(r => r.Kind == NodeKind.InlineCode && r.Text == "code");
        runIndex.Should().BeGreaterThanOrEqualTo(0);

        var run = line.Runs[runIndex];
        var anchor = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 0, CaretX: MarkdownHitTester.GetCaretX(run, 0));
        var active = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 4, CaretX: MarkdownHitTester.GetCaretX(run, 4));
        var sel = new SelectionRange(anchor, active);

        SelectionSourceMapper.TryMapToSource(markdown, doc, sel, out var sourceSel).Should().BeTrue();
        sourceSel.Slice(markdown).Should().Be("code");
    }

    [Test]
    public void Source_mapping_for_fenced_code_block_maps_to_code_content()
    {
        var markdown = "```csharp\nvar x = 1;\nvar y = 2;\n```";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var code = layout.Blocks.OfType<CodeBlockLayout>().Single();
        code.Lines.Length.Should().BeGreaterThanOrEqualTo(2);

        var firstLine = code.Lines[0];
        var firstRun = firstLine.Runs.Single();
        firstRun.IsCodeBlockLine.Should().BeTrue();

        // Select the character 'x' in "var x = 1;".
        var offset = firstRun.Text.IndexOf('x');
        offset.Should().BeGreaterThan(0);

        var anchor = new MarkdownHitTestResult(0, 0, firstRun, firstLine, TextOffset: offset, CaretX: MarkdownHitTester.GetCaretX(firstRun, offset));
        var active = new MarkdownHitTestResult(0, 0, firstRun, firstLine, TextOffset: offset + 1, CaretX: MarkdownHitTester.GetCaretX(firstRun, offset + 1));
        var sel = new SelectionRange(anchor, active);

        SelectionSourceMapper.TryMapToSource(markdown, doc, sel, out var sourceSel).Should().BeTrue();
        sourceSel.Slice(markdown).Should().Be("x");
    }
}
