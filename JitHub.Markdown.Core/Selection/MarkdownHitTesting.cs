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

        var index = MarkdownLineIndexCache.Get(layout);
        var lines = index.Lines;
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            if (y >= line.Y && y <= (line.Y + line.Height))
            {
                if (TryHitTestLine(lineIndex, line, x, out result))
                {
                    return true;
                }

                // Line exists but has no runs: no hit.
                break;
            }
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

        var index = MarkdownLineIndexCache.Get(layout);
        var bands = index.NonEmptyBands;
        if (bands.Length == 0)
        {
            result = default;
            return false;
        }

        var bandIndex = FindNearestBandIndex(bands, y);
        var band = bands[bandIndex];
        return TryHitTestLine(band.LineIndex, band.Line, x, out result);
    }

    internal static bool TryHitTestLine(int lineIndex, LineLayout line, float x, out MarkdownHitTestResult result)
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

    private static int FindNearestBandIndex(ImmutableArray<LineBand> bands, float y)
    {
        // bands are ordered top-to-bottom by construction.
        // Find the last band with Top <= y.
        var lo = 0;
        var hi = bands.Length - 1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (bands[mid].Top <= y)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        var idx = hi;
        if (idx < 0)
        {
            return 0;
        }

        var current = bands[idx];
        if (y <= current.Bottom)
        {
            return idx;
        }

        if (idx >= bands.Length - 1)
        {
            return bands.Length - 1;
        }

        var next = bands[idx + 1];
        var distDown = y - current.Bottom;
        var distUp = next.Top - y;
        return distUp < distDown ? idx + 1 : idx;
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
            var visualOffset = (int)MathF.Round(t * run.Text.Length);
            if (run.IsRightToLeft)
            {
                return Math.Clamp(run.Text.Length - visualOffset, 0, run.Text.Length);
            }

            return Math.Clamp(visualOffset, 0, run.Text.Length);
        }

        // gx contains absolute x boundaries for each *visual* offset (increasing X).
        if (x <= gx[0])
        {
            return run.IsRightToLeft ? run.Text.Length : 0;
        }

        var last = gx[gx.Length - 1];
        if (x >= last)
        {
            return run.IsRightToLeft ? 0 : run.Text.Length;
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

        var visual = Math.Clamp(lo - 1, 0, run.Text.Length);
        if (run.IsRightToLeft)
        {
            return Math.Clamp(run.Text.Length - visual, 0, run.Text.Length);
        }

        return visual;
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
            if (run.Text.Length == 0)
            {
                return run.Bounds.X;
            }

            var visualOffset = run.IsRightToLeft
                ? Math.Clamp(run.Text.Length - textOffset, 0, run.Text.Length)
                : Math.Clamp(textOffset, 0, run.Text.Length);

            var t = Math.Clamp((float)visualOffset / run.Text.Length, 0f, 1f);
            return run.Bounds.X + (w * t);
        }

        var i = run.IsRightToLeft
            ? Math.Clamp((run.Text?.Length ?? 0) - textOffset, 0, gx.Length - 1)
            : Math.Clamp(textOffset, 0, gx.Length - 1);
        return gx[i];
    }

    // Note: line enumeration is centralized in MarkdownLineIndexCache.
}
