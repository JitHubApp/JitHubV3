using System.Collections.Immutable;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace JitHub.Markdown;

internal sealed class DocumentBuilder
{
    private readonly bool _allowHtml;

    public DocumentBuilder(bool allowHtml)
    {
        _allowHtml = allowHtml;
    }

    public MarkdownDocumentModel Build(string sourceMarkdown, MarkdownDocument ast)
    {
        var sourceMapEntries = new List<SourceSpanEntry>(capacity: 512);

        var documentId = new NodeId(0);
        var blocks = ImmutableArray.CreateBuilder<BlockNode>(ast.Count);
        for (var i = 0; i < ast.Count; i++)
        {
            if (ast[i] is not Block block)
            {
                continue;
            }

            var node = ConvertBlock(sourceMarkdown, block, i, documentId, sourceMapEntries);
            if (node is not null)
            {
                blocks.Add(node);
            }
        }

        return new MarkdownDocumentModel(
            sourceMarkdown,
            blocks.ToImmutable(),
            new SourceMap(sourceMapEntries));
    }

    private BlockNode? ConvertBlock(
        string source,
        Block block,
        int ordinal,
        NodeId parentId,
        List<SourceSpanEntry> sourceMapEntries)
    {
        var span = GetSpan(block);

        // Respect HTML policy. Markdig still parses HTML, but we can drop it here.
        if (!_allowHtml && block is HtmlBlock)
        {
            return null;
        }

        switch (block)
        {
            case HeadingBlock heading:
            {
                var id = NodeId.Create(NodeKind.Heading, span, ordinal, parentId);
                var inlines = ConvertInlines(source, heading.Inline, id, sourceMapEntries);
                var node = new HeadingBlockNode(id, span, heading.Level, inlines);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case ParagraphBlock paragraph:
            {
                var id = NodeId.Create(NodeKind.Paragraph, span, ordinal, parentId);
                var inlines = ConvertInlines(source, paragraph.Inline, id, sourceMapEntries);
                var node = new ParagraphBlockNode(id, span, inlines);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case QuoteBlock quote:
            {
                var id = NodeId.Create(NodeKind.BlockQuote, span, ordinal, parentId);
                var inner = ImmutableArray.CreateBuilder<BlockNode>(quote.Count);
                for (var i = 0; i < quote.Count; i++)
                {
                    if (quote[i] is Block child)
                    {
                        var converted = ConvertBlock(source, child, i, id, sourceMapEntries);
                        if (converted is not null)
                        {
                            inner.Add(converted);
                        }
                    }
                }

                var node = new BlockQuoteBlockNode(id, span, inner.ToImmutable());
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case ListBlock list:
            {
                var id = NodeId.Create(NodeKind.List, span, ordinal, parentId);
                var items = ImmutableArray.CreateBuilder<ListItemBlockNode>(list.Count);

                var itemOrdinal = 0;
                foreach (var child in list)
                {
                    if (child is ListItemBlock listItem)
                    {
                        var converted = ConvertListItem(source, listItem, itemOrdinal++, id, sourceMapEntries);
                        items.Add(converted);
                    }
                }

                var node = new ListBlockNode(id, span, list.IsOrdered, items.ToImmutable());
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case FencedCodeBlock fenced:
            {
                var id = NodeId.Create(NodeKind.CodeBlock, span, ordinal, parentId);
                var code = fenced.Lines.ToString() ?? string.Empty;
                var info = fenced.Info;
                var node = new CodeBlockNode(id, span, string.IsNullOrWhiteSpace(info) ? null : info, code);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case CodeBlock codeBlock:
            {
                var id = NodeId.Create(NodeKind.CodeBlock, span, ordinal, parentId);
                var code = codeBlock.Lines.ToString() ?? string.Empty;
                var node = new CodeBlockNode(id, span, Info: null, code);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case Table table:
            {
                var id = NodeId.Create(NodeKind.Table, span, ordinal, parentId);

                var rows = ImmutableArray.CreateBuilder<TableRowBlockNode>(table.Count);
                for (var rowOrdinal = 0; rowOrdinal < table.Count; rowOrdinal++)
                {
                    if (table[rowOrdinal] is not TableRow row)
                    {
                        continue;
                    }

                    var rowSpan = GetSpan(row);
                    var rowId = NodeId.Create(NodeKind.TableRow, rowSpan, rowOrdinal, id);

                    var cells = ImmutableArray.CreateBuilder<TableCellBlockNode>(row.Count);
                    var cellOrdinal = 0;
                    foreach (var c in row)
                    {
                        if (c is not TableCell cell)
                        {
                            continue;
                        }

                        var cellSpan = GetSpan(cell);
                        var cellId = NodeId.Create(NodeKind.TableCell, cellSpan, cellOrdinal++, rowId);

                        var blocks = ImmutableArray.CreateBuilder<BlockNode>(cell.Count);
                        for (var bi = 0; bi < cell.Count; bi++)
                        {
                            if (cell[bi] is Block child)
                            {
                                var converted = ConvertBlock(source, child, bi, cellId, sourceMapEntries);
                                if (converted is not null)
                                {
                                    blocks.Add(converted);
                                }
                            }
                        }

                        var cellNode = new TableCellBlockNode(cellId, cellSpan, blocks.ToImmutable());
                        sourceMapEntries.Add(new SourceSpanEntry(cellId, cellNode.Kind, cellNode.Span));
                        cells.Add(cellNode);
                    }

                    var rowNode = new TableRowBlockNode(rowId, rowSpan, cells.ToImmutable());
                    sourceMapEntries.Add(new SourceSpanEntry(rowId, rowNode.Kind, rowNode.Span));
                    rows.Add(rowNode);
                }

                var node = new TableBlockNode(id, span, rows.ToImmutable());
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case ThematicBreakBlock:
            {
                var id = NodeId.Create(NodeKind.ThematicBreak, span, ordinal, parentId);
                var node = new ThematicBreakBlockNode(id, span);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            default:
            {
                // Unknown blocks are intentionally ignored in Phase 1. We'll expand coverage in Phase 2.
                return null;
            }
        }
    }

    private ListItemBlockNode ConvertListItem(
        string source,
        ListItemBlock listItem,
        int ordinal,
        NodeId parentId,
        List<SourceSpanEntry> sourceMapEntries)
    {
        var span = GetSpan(listItem);
        var id = NodeId.Create(NodeKind.ListItem, span, ordinal, parentId);

        var blocks = ImmutableArray.CreateBuilder<BlockNode>(listItem.Count);
        for (var i = 0; i < listItem.Count; i++)
        {
            if (listItem[i] is Block child)
            {
                var converted = ConvertBlock(source, child, i, id, sourceMapEntries);
                if (converted is not null)
                {
                    blocks.Add(converted);
                }
            }
        }

        var (isTask, isChecked) = TryDetectTaskState(source, span);

        var node = new ListItemBlockNode(id, span, isTask, isChecked, blocks.ToImmutable());
        sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
        return node;
    }

    private static (bool IsTask, bool? IsChecked) TryDetectTaskState(string source, SourceSpan span)
    {
        if (span.IsEmpty || span.Start < 0 || span.EndExclusive > source.Length)
        {
            return (false, null);
        }

        var text = source.Substring(span.Start, span.Length);

        // Simple heuristic for Phase 1: detect task list markers like "- [x]" or "* [ ]".
        // This is sufficient for consistent selection/source mapping tests and can be replaced
        // by Markdig task list metadata later.
        var idx = text.IndexOf("[", StringComparison.Ordinal);
        if (idx < 0 || idx + 2 >= text.Length)
        {
            return (false, null);
        }

        if (idx + 2 < text.Length && text[idx] == '[' && text[idx + 2] == ']')
        {
            var mid = text[idx + 1];
            if (mid == ' ')
            {
                return (true, false);
            }

            if (mid == 'x' || mid == 'X')
            {
                return (true, true);
            }
        }

        return (false, null);
    }

    private static ImmutableArray<InlineNode> ConvertInlines(
        string source,
        ContainerInline? container,
        NodeId parentId,
        List<SourceSpanEntry> sourceMapEntries)
    {
        if (container is null)
        {
            return ImmutableArray<InlineNode>.Empty;
        }

        var list = ImmutableArray.CreateBuilder<InlineNode>();
        var ordinal = 0;

        var current = container.FirstChild;
        while (current is not null)
        {
            var converted = ConvertInline(source, current, ordinal++, parentId, sourceMapEntries);
            if (converted is not null)
            {
                list.Add(converted);
            }

            current = current.NextSibling;
        }

        return list.ToImmutable();
    }

    private static InlineNode? ConvertInline(
        string source,
        Inline inline,
        int ordinal,
        NodeId parentId,
        List<SourceSpanEntry> sourceMapEntries)
    {
        var span = GetSpan(inline);

        switch (inline)
        {
            case LiteralInline literal:
            {
                var id = NodeId.Create(NodeKind.Text, span, ordinal, parentId);
                var text = literal.Content.ToString() ?? string.Empty;
                var node = new TextInlineNode(id, span, text);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case LineBreakInline:
            {
                var id = NodeId.Create(NodeKind.LineBreak, span, ordinal, parentId);
                var node = new LineBreakInlineNode(id, span);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case CodeInline code:
            {
                var id = NodeId.Create(NodeKind.InlineCode, span, ordinal, parentId);
                var node = new InlineCodeNode(id, span, code.Content);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case LinkInline link:
            {
                var isImage = link.IsImage;
                var kind = isImage ? NodeKind.Image : NodeKind.Link;
                var id = NodeId.Create(kind, span, ordinal, parentId);
                var children = ConvertInlines(source, link, id, sourceMapEntries);
                InlineNode node = isImage
                    ? new ImageInlineNode(id, span, link.Url, link.Title, children)
                    : new LinkInlineNode(id, span, link.Url, link.Title, children);
                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            case EmphasisInline emphasis:
            {
                var kind = emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2
                    ? NodeKind.Strikethrough
                    : emphasis.DelimiterCount >= 2
                        ? NodeKind.Strong
                        : NodeKind.Emphasis;
                var id = NodeId.Create(kind, span, ordinal, parentId);
                var children = ConvertInlines(source, emphasis, id, sourceMapEntries);

                InlineNode node = kind switch
                {
                    NodeKind.Strong => new StrongInlineNode(id, span, children),
                    NodeKind.Strikethrough => new StrikethroughInlineNode(id, span, children),
                    _ => new EmphasisInlineNode(id, span, children)
                };

                sourceMapEntries.Add(new SourceSpanEntry(id, node.Kind, node.Span));
                return node;
            }

            default:
                return null;
        }
    }

    private static SourceSpan GetSpan(MarkdownObject obj)
    {
        var span = obj.Span;
        return SourceSpan.FromInclusive(span.Start, span.End);
    }
}
