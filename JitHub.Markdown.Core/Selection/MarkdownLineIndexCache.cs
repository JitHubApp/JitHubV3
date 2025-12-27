using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace JitHub.Markdown;

internal sealed class MarkdownLineIndex
{
    public required ImmutableArray<LineLayout> Lines { get; init; }

    public required ImmutableArray<LineBand> NonEmptyBands { get; init; }
}

internal readonly record struct LineBand(int LineIndex, LineLayout Line, float Top, float Bottom);

internal static class MarkdownLineIndexCache
{
    private static readonly ConditionalWeakTable<MarkdownLayout, MarkdownLineIndex> _cache = new();

    public static MarkdownLineIndex Get(MarkdownLayout layout)
    {
        _ = layout ?? throw new ArgumentNullException(nameof(layout));
        return _cache.GetValue(layout, Build);
    }

    private static MarkdownLineIndex Build(MarkdownLayout layout)
    {
        var linesBuilder = ImmutableArray.CreateBuilder<LineLayout>();
        var bandsBuilder = ImmutableArray.CreateBuilder<LineBand>();

        var lineIndex = 0;
        foreach (var line in EnumerateLines(layout))
        {
            linesBuilder.Add(line);

            if (line.Runs.Length > 0)
            {
                var top = line.Y;
                var bottom = line.Y + line.Height;
                bandsBuilder.Add(new LineBand(lineIndex, line, top, bottom));
            }

            lineIndex++;
        }

        return new MarkdownLineIndex
        {
            Lines = linesBuilder.ToImmutable(),
            NonEmptyBands = bandsBuilder.ToImmutable(),
        };
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
