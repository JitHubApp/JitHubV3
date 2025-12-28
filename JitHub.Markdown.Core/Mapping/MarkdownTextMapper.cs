using System.Text;

namespace JitHub.Markdown;

internal static class MarkdownTextMapper
{
    public static TextOffsetMap BuildForInlines(
        string sourceMarkdown,
        IReadOnlyList<InlineNode> inlines,
        MarkdownSpanMappingPolicy? policy = null)
    {
        policy ??= new MarkdownSpanMappingPolicy();

        var sb = new StringBuilder();
        var map = new List<int>();

        foreach (var inline in inlines)
        {
            AppendInline(sourceMarkdown, inline, policy, sb, map);
        }

        return new TextOffsetMap(sb.ToString(), map.ToArray());
    }

    private static void AppendInline(
        string source,
        InlineNode inline,
        MarkdownSpanMappingPolicy policy,
        StringBuilder sb,
        List<int> map)
    {
        switch (inline)
        {
            case TextInlineNode text:
                AppendText(text.Text, text.Span, sb, map);
                return;

            case LineBreakInlineNode:
                sb.Append('\n');
                map.Add(-1);
                return;

            case InlineCodeNode code:
                AppendInlineCode(source, code, sb, map);
                return;

            case LinkInlineNode link:
                foreach (var child in link.Inlines)
                {
                    AppendInline(source, child, policy, sb, map);
                }
                return;

            case ImageInlineNode image:
                // Phase 1: treat visible text as alt text.
                foreach (var child in image.AltText)
                {
                    AppendInline(source, child, policy, sb, map);
                }
                return;

            case EmphasisInlineNode emph:
            case StrongInlineNode strong:
            case StrikethroughInlineNode strike:
                // Phase 1 default: map to inner content.
                // NodeSpan mode is deferred until we have run-level layout/selection.
                var children = inline switch
                {
                    EmphasisInlineNode e => e.Inlines,
                    StrongInlineNode s => s.Inlines,
                    StrikethroughInlineNode st => st.Inlines,
                    _ => throw new NotSupportedException()
                };

                foreach (var child in children)
                {
                    AppendInline(source, child, policy, sb, map);
                }
                return;

            default:
                return;
        }
    }

    private static void AppendText(string text, SourceSpan span, StringBuilder sb, List<int> map)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        sb.Append(text);

        // Best-effort char-level mapping.
        for (var i = 0; i < text.Length; i++)
        {
            map.Add(span.Start + i);
        }
    }

    private static void AppendInlineCode(string source, InlineCodeNode code, StringBuilder sb, List<int> map)
    {
        sb.Append(code.Code);

        if (code.Span.IsEmpty || code.Span.Start < 0 || code.Span.EndExclusive > source.Length)
        {
            for (var i = 0; i < code.Code.Length; i++)
            {
                map.Add(-1);
            }
            return;
        }

        var raw = source.Substring(code.Span.Start, code.Span.Length);
        var innerIdx = raw.IndexOf(code.Code, StringComparison.Ordinal);

        if (innerIdx < 0)
        {
            for (var i = 0; i < code.Code.Length; i++)
            {
                map.Add(code.Span.Start);
            }
            return;
        }

        var innerStart = code.Span.Start + innerIdx;
        for (var i = 0; i < code.Code.Length; i++)
        {
            map.Add(innerStart + i);
        }
    }
}
