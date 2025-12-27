using System.Collections.Immutable;

namespace JitHub.Markdown;

public readonly record struct MarkdownHitTestResult(
    int LineIndex,
    int RunIndex,
    InlineRunLayout Run,
    LineLayout Line,
    int TextOffset,
    float CaretX);

public static class MarkdownHitTester
{
    public static bool TryHitTest(MarkdownLayout layout, float x, float y, out MarkdownHitTestResult result)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));

        var lineIndex = 0;
        foreach (var line in EnumerateLines(layout))
        {
            if (y >= line.Y && y <= (line.Y + line.Height))
            {
                if (TryHitTestLine(lineIndex, line, x, out result))
                {
                    return true;
                }

                // Line exists but has no runs: no hit.
                break;
            }

            lineIndex++;
        }

        result = default;
        return false;
    }

    /// <summary>
    /// Attempts to hit-test the layout at the given coordinates. If the Y coordinate falls between
    /// lines (e.g., block spacing), this method clamps to the nearest line so clicks in vertical
    /// gaps still map to a valid caret/selection position.
    /// </summary>
    public static bool TryHitTestNearest(MarkdownLayout layout, float x, float y, out MarkdownHitTestResult result)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));

        var bestLineIndex = -1;
        LineLayout? bestLine = null;
        var bestDistance = float.PositiveInfinity;

        var lineIndex = 0;
        foreach (var line in EnumerateLines(layout))
        {
            // Skip empty lines (no runs) when searching for a usable caret target.
            if (line.Runs.Length == 0)
            {
                lineIndex++;
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
                // Within this line's vertical band.
                distance = 0f;
            }

            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestLineIndex = lineIndex;
                bestLine = line;

                // Can't do better than an exact match.
                if (distance == 0f)
                {
                    break;
                }
            }

            lineIndex++;
        }

        if (bestLineIndex >= 0 && bestLine is not null)
        {
            return TryHitTestLine(bestLineIndex, bestLine, x, out result);
        }

        result = default;
        return false;
    }

    private static bool TryHitTestLine(int lineIndex, LineLayout line, float x, out MarkdownHitTestResult result)
    {
        if (line.Runs.Length == 0)
        {
            result = default;
            return false;
        }

        var runIndex = FindRunIndex(line.Runs, x);
        var run = line.Runs[runIndex];

        var textOffset = GetTextOffset(run, x);
        var caretX = GetCaretX(run, textOffset);

        result = new MarkdownHitTestResult(
            LineIndex: lineIndex,
            RunIndex: runIndex,
            Run: run,
            Line: line,
            TextOffset: textOffset,
            CaretX: caretX);

        return true;
    }

    private static int FindRunIndex(ImmutableArray<InlineRunLayout> runs, float x)
    {
        // Prefer containment.
        for (var i = 0; i < runs.Length; i++)
        {
            var r = runs[i].Bounds;
            if (x >= r.X && x <= r.Right)
            {
                return i;
            }
        }

        // Otherwise clamp to nearest.
        if (x < runs[0].Bounds.X)
        {
            return 0;
        }

        return runs.Length - 1;
    }

    private static int GetTextOffset(InlineRunLayout run, float x)
    {
        if (string.IsNullOrEmpty(run.Text))
        {
            return 0;
        }

        if (run.Kind == NodeKind.Image)
        {
            return 0;
        }

        var gx = run.GlyphX;
        if (gx.IsDefault || gx.Length == 0)
        {
            // Fallback: proportional mapping across the run width.
            var w = Math.Max(1f, run.Bounds.Width);
            var t = Math.Clamp((x - run.Bounds.X) / w, 0f, 1f);
            return (int)MathF.Round(t * run.Text.Length);
        }

        // gx contains absolute x boundaries for each text offset.
        if (x <= gx[0])
        {
            return 0;
        }

        var last = gx[gx.Length - 1];
        if (x >= last)
        {
            return run.Text.Length;
        }

        // Find the first boundary strictly greater than x.
        // Offset is the index of the preceding boundary.
        var lo = 0;
        var hi = gx.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (gx[mid] <= x)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return Math.Clamp(lo - 1, 0, run.Text.Length);
    }

    public static float GetCaretX(InlineRunLayout run, int textOffset)
    {
        if (run.Kind == NodeKind.Image)
        {
            return run.Bounds.X;
        }

        var gx = run.GlyphX;
        if (gx.IsDefault || gx.Length == 0)
        {
            var w = Math.Max(0, run.Bounds.Width);
            var t = run.Text.Length == 0 ? 0 : Math.Clamp((float)textOffset / run.Text.Length, 0f, 1f);
            return run.Bounds.X + (w * t);
        }

        var i = Math.Clamp(textOffset, 0, gx.Length - 1);
        return gx[i];
    }

    private static IEnumerable<LineLayout> EnumerateLines(MarkdownLayout layout)
    {
        for (var i = 0; i < layout.Blocks.Length; i++)
        {
            foreach (var line in EnumerateLines(layout.Blocks[i]))
            {
                yield return line;
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
}
