namespace JitHub.Markdown;

using System.Linq;

public readonly record struct SourceSelection(int Start, int EndExclusive)
{
    public int Length => EndExclusive - Start;

    public bool IsEmpty => Length <= 0;

    public string Slice(string markdown)
    {
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));
        if (Start < 0 || Start > markdown.Length) return string.Empty;
        if (EndExclusive < 0 || EndExclusive > markdown.Length) return string.Empty;
        if (EndExclusive <= Start) return string.Empty;
        return markdown.Substring(Start, EndExclusive - Start);
    }
}

public static class SelectionSourceMapper
{
    public static bool TryMapToSource(
        string sourceMarkdown,
        MarkdownDocumentModel document,
        SelectionRange range,
        out SourceSelection selection,
        ISelectionNormalizer? normalizer = null)
    {
        if (sourceMarkdown is null) throw new ArgumentNullException(nameof(sourceMarkdown));
        if (document is null) throw new ArgumentNullException(nameof(document));

        // Normalize without requiring a layout instance.
        var normalized = SelectionRange.Compare(range.Anchor, range.Active) <= 0
            ? range
            : new SelectionRange(range.Active, range.Anchor);

        var start = normalized.Start;
        var end = normalized.End;

        if (!TryMapCaretToSourceIndex(sourceMarkdown, document, start.Run, start.TextOffset, out var startIndex))
        {
            selection = default;
            return false;
        }

        if (!TryMapCaretToSourceIndex(sourceMarkdown, document, end.Run, end.TextOffset, out var endIndex))
        {
            selection = default;
            return false;
        }

        startIndex = Math.Clamp(startIndex, 0, sourceMarkdown.Length);
        endIndex = Math.Clamp(endIndex, 0, sourceMarkdown.Length);

        if (endIndex < startIndex)
        {
            (startIndex, endIndex) = (endIndex, startIndex);
        }

        selection = new SourceSelection(startIndex, endIndex);
        return true;
    }

    private static bool TryMapCaretToSourceIndex(
        string sourceMarkdown,
        MarkdownDocumentModel document,
        InlineRunLayout run,
        int textOffset,
        out int sourceIndex)
    {
        sourceIndex = 0;

        if (run.Kind == NodeKind.Image)
        {
            sourceIndex = Math.Clamp(run.Span.Start, 0, sourceMarkdown.Length);
            return true;
        }

        if (run.IsCodeBlockLine)
        {
            var codeBlock = FindCodeBlock(document, run.Id);
            if (codeBlock is null)
            {
                sourceIndex = Math.Clamp(run.Span.Start, 0, sourceMarkdown.Length);
                return true;
            }

            if (!TryFindNodeContentStart(sourceMarkdown, codeBlock.Span, codeBlock.Code, out var contentStart))
            {
                sourceIndex = Math.Clamp(codeBlock.Span.Start, 0, sourceMarkdown.Length);
                return true;
            }

            sourceIndex = contentStart + Math.Max(0, run.NodeTextOffset) + Math.Clamp(textOffset, 0, run.Text.Length);
            return true;
        }

        // Inline code wants mapping to the inner code, not the backticks.
        if (run.Kind == NodeKind.InlineCode)
        {
            if (TryFindNodeContentStart(sourceMarkdown, run.Span, run.Text, out var innerStart))
            {
                sourceIndex = innerStart + Math.Clamp(textOffset, 0, run.Text.Length);
                return true;
            }
        }

        // Default: best-effort direct span mapping.
        var baseStart = run.Span.Start;
        if (baseStart < 0)
        {
            sourceIndex = 0;
            return true;
        }

        var offset = Math.Clamp(textOffset, 0, run.Text?.Length ?? 0);
        sourceIndex = baseStart + offset;
        return true;
    }

    private static bool TryFindNodeContentStart(string source, SourceSpan nodeSpan, string content, out int contentStart)
    {
        contentStart = 0;

        if (nodeSpan.IsEmpty || nodeSpan.Start < 0 || nodeSpan.EndExclusive > source.Length)
        {
            return false;
        }

        var raw = source.Substring(nodeSpan.Start, nodeSpan.Length);
        var idx = raw.IndexOf(content, StringComparison.Ordinal);
        if (idx < 0)
        {
            return false;
        }

        contentStart = nodeSpan.Start + idx;
        return true;
    }

    private static CodeBlockNode? FindCodeBlock(MarkdownDocumentModel document, NodeId id)
    {
        for (var i = 0; i < document.Blocks.Length; i++)
        {
            var found = FindCodeBlock(document.Blocks[i], id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static CodeBlockNode? FindCodeBlock(BlockNode block, NodeId id)
    {
        if (block is CodeBlockNode cb && cb.Id == id)
        {
            return cb;
        }

        return block switch
        {
            BlockQuoteBlockNode q => q.Blocks.Select(b => FindCodeBlock(b, id)).FirstOrDefault(b => b is not null),
            ListBlockNode l => l.Items.SelectMany(i => i.Blocks).Select(b => FindCodeBlock(b, id)).FirstOrDefault(b => b is not null),
            ListItemBlockNode li => li.Blocks.Select(b => FindCodeBlock(b, id)).FirstOrDefault(b => b is not null),
            TableBlockNode t => t.Rows.SelectMany(r => r.Cells).SelectMany(c => c.Blocks).Select(b => FindCodeBlock(b, id)).FirstOrDefault(b => b is not null),
            TableCellBlockNode tc => tc.Blocks.Select(b => FindCodeBlock(b, id)).FirstOrDefault(b => b is not null),
            _ => null,
        };
    }
}
