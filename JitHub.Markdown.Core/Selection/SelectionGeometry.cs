using System.Collections.Immutable;

namespace JitHub.Markdown;

public sealed class SelectionGeometry
{
    public required ImmutableArray<RectF> Rects { get; init; }
}

public static class SelectionGeometryBuilder
{
    public static SelectionGeometry Build(MarkdownLayout layout, SelectionRange range, ISelectionNormalizer? normalizer = null)
    {
        if (layout is null) throw new ArgumentNullException(nameof(layout));

        normalizer ??= DefaultSelectionNormalizer.Instance;
        var normalized = normalizer.Normalize(layout, range);

        var start = normalized.Start;
        var end = normalized.End;

        var rects = ImmutableArray.CreateBuilder<RectF>();

        var lineIndex = 0;
        foreach (var line in EnumerateLines(layout))
        {
            if (lineIndex < start.LineIndex)
            {
                lineIndex++;
                continue;
            }

            if (lineIndex > end.LineIndex)
            {
                break;
            }

            if (line.Runs.Length == 0)
            {
                lineIndex++;
                continue;
            }

            var (lineMinX, lineMaxX) = GetLineExtents(line);

            var x1 = lineMinX;
            var x2 = lineMaxX;

            if (start.LineIndex == end.LineIndex)
            {
                // Single line selection.
                x1 = start.CaretX;
                x2 = end.CaretX;
            }
            else if (lineIndex == start.LineIndex)
            {
                x1 = start.CaretX;
            }
            else if (lineIndex == end.LineIndex)
            {
                x2 = end.CaretX;
            }

            if (x2 < x1)
            {
                (x1, x2) = (x2, x1);
            }

            var w = Math.Max(0, x2 - x1);
            if (w > 0)
            {
                rects.Add(new RectF(x1, line.Y, w, line.Height));
            }

            lineIndex++;
        }

        return new SelectionGeometry { Rects = rects.ToImmutable() };
    }

    private static (float minX, float maxX) GetLineExtents(LineLayout line)
    {
        var minX = float.PositiveInfinity;
        var maxX = float.NegativeInfinity;

        for (var i = 0; i < line.Runs.Length; i++)
        {
            var r = line.Runs[i].Bounds;
            minX = Math.Min(minX, r.X);
            maxX = Math.Max(maxX, r.Right);
        }

        if (!float.IsFinite(minX) || !float.IsFinite(maxX) || maxX < minX)
        {
            return (0, 0);
        }

        return (minX, maxX);
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
