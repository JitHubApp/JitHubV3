using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownSelectionTests
{
    private static IEnumerable<(int lineIndex, LineLayout line)> EnumerateLinesWithIndex(MarkdownLayout layout)
    {
        var i = 0;
        for (var bi = 0; bi < layout.Blocks.Length; bi++)
        {
            foreach (var line in EnumerateLines(layout.Blocks[bi]))
            {
                yield return (i, line);
                i++;
            }
        }
    }

    private static IEnumerable<LineLayout> EnumerateLines(BlockLayout block)
    {
        switch (block)
        {
            case ParagraphLayout p:
                foreach (var l in p.Lines) yield return l;
                yield break;

            case HeadingLayout h:
                foreach (var l in h.Lines) yield return l;
                yield break;

            case CodeBlockLayout c:
                foreach (var l in c.Lines) yield return l;
                yield break;

            case BlockQuoteLayout q:
                foreach (var child in q.Blocks)
                {
                    foreach (var l in EnumerateLines(child)) yield return l;
                }
                yield break;

            case ListLayout l:
                foreach (var item in l.Items)
                {
                    foreach (var ll in EnumerateLines(item)) yield return ll;
                }
                yield break;

            case ListItemLayout li:
                foreach (var child in li.Blocks)
                {
                    foreach (var ll in EnumerateLines(child)) yield return ll;
                }
                yield break;

            case TableLayout t:
                for (var r = 0; r < t.Rows.Length; r++)
                {
                    var row = t.Rows[r];
                    for (var c = 0; c < row.Cells.Length; c++)
                    {
                        var cell = row.Cells[c];
                        for (var bi = 0; bi < cell.Blocks.Length; bi++)
                        {
                            foreach (var ll in EnumerateLines(cell.Blocks[bi])) yield return ll;
                        }
                    }
                }
                yield break;

            default:
                yield break;
        }
    }

    private static bool ReferenceTryHitTestNearest(MarkdownLayout layout, float x, float y, out MarkdownHitTestResult result)
    {
        var bestLineIndex = -1;
        LineLayout? bestLine = null;
        var bestDistance = float.PositiveInfinity;

        foreach (var (lineIndex, line) in EnumerateLinesWithIndex(layout))
        {
            if (line.Runs.Length == 0)
            {
                continue;
            }

            var top = line.Y;
            var bottom = line.Y + line.Height;

            float distance;
            if (y < top)
            {
                distance = top - y;
            }
            else if (y > bottom)
            {
                distance = y - bottom;
            }
            else
            {
                distance = 0f;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestLineIndex = lineIndex;
                bestLine = line;

                if (distance == 0f)
                {
                    break;
                }
            }
        }

        if (bestLineIndex >= 0 && bestLine is not null)
        {
            return MarkdownHitTester.TryHitTestLine(bestLineIndex, bestLine, x, out result);
        }

        result = default;
        return false;
    }

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
    public void Hit_test_can_reach_end_of_run_near_last_glyph()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Hello world");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var worldRun = line.Runs.First(r => r.Text == "world");

        worldRun.GlyphX.IsDefault.Should().BeFalse();
        worldRun.GlyphX.Length.Should().Be(worldRun.Text.Length + 1);

        var gx = worldRun.GlyphX;
        var x = (gx[gx.Length - 2] + gx[gx.Length - 1]) / 2f + 0.01f;
        var y = line.Y + (line.Height / 2f);

        MarkdownHitTester.TryHitTest(layout, x, y, out var hit).Should().BeTrue();
        hit.Run.Text.Should().Be("world");
        hit.TextOffset.Should().Be(worldRun.Text.Length);
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
    public void Link_activation_does_not_trigger_when_clicking_past_link_bounds()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Go to [Link](https://example.com)");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var linkRun = line.Runs.Single(r => r.Kind == NodeKind.Link);

        var y = line.Y + (line.Height / 2f);
        var xOutside = linkRun.Bounds.Right + 40f;

        MarkdownHitTester.TryHitTestNearest(layout, xOutside, y, out var hit).Should().BeTrue();
        hit.Run.Kind.Should().Be(NodeKind.Link, "nearest hit-test clamps X to the last run");

        var interaction = new SelectionPointerInteraction();
        var down = interaction.OnPointerDown(hit, x: xOutside, y: y, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false));
        down.ActivateLinkUrl.Should().BeNull();

        var up = interaction.OnPointerUp(hit, selectionEnabled: true);
        up.ActivateLinkUrl.Should().BeNull("clicking outside link bounds must not activate");
    }

    [Test]
    public void Hit_test_nearest_matches_reference_scan_for_many_y_samples()
    {
        var markdown = "First paragraph with a link [A](https://example.com).\n\nSecond paragraph with a link [B](https://example.com).\n\nThird paragraph.";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 320, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var allLines = EnumerateLinesWithIndex(layout).ToList();
        allLines.Count.Should().BeGreaterThan(0);

        // Pick an X that is likely to land inside text for most lines.
        var firstNonEmpty = allLines.First(l => l.line.Runs.Length > 0);
        var firstRun = firstNonEmpty.line.Runs.First(r => !string.IsNullOrEmpty(r.Text) || r.Kind == NodeKind.Image);
        var x = (firstRun.Bounds.X + firstRun.Bounds.Right) / 2f;

        var minY = allLines.Min(l => l.line.Y) - 40;
        var maxY = allLines.Max(l => l.line.Y + l.line.Height) + 40;

        for (var y = minY; y <= maxY; y += 7.5f)
        {
            var ok1 = MarkdownHitTester.TryHitTestNearest(layout, x, y, out var h1);
            var ok2 = ReferenceTryHitTestNearest(layout, x, y, out var h2);

            ok1.Should().Be(ok2, $"y={y}");
            if (ok1)
            {
                h1.LineIndex.Should().Be(h2.LineIndex, $"y={y}");
                h1.RunIndex.Should().Be(h2.RunIndex, $"y={y}");
                h1.TextOffset.Should().Be(h2.TextOffset, $"y={y}");
            }
        }
    }

    [Test]
    public void Hit_test_nearest_prefers_earlier_line_on_exact_tie_in_gap()
    {
        var markdown = "First paragraph.\n\nSecond paragraph.";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var nonEmpty = EnumerateLinesWithIndex(layout).Where(l => l.line.Runs.Length > 0).ToList();
        nonEmpty.Count.Should().BeGreaterThanOrEqualTo(2);

        var first = nonEmpty[0];
        var second = nonEmpty[1];

        var gapTop = first.line.Y + first.line.Height;
        var gapBottom = second.line.Y;

        (gapBottom > gapTop).Should().BeTrue("paragraph spacing should create a vertical gap between the two lines");

        var yMid = (gapTop + gapBottom) / 2f;
        var run = first.line.Runs[0];
        var x = (run.Bounds.X + run.Bounds.Right) / 2f;

        MarkdownHitTester.TryHitTestNearest(layout, x, yMid, out var hit).Should().BeTrue();
        hit.LineIndex.Should().Be(first.lineIndex, "on an exact distance tie, nearest hit-test should keep the first encountered line");
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
