using System.Collections.Immutable;

namespace JitHub.Markdown;

public abstract record Node(NodeId Id, NodeKind Kind, SourceSpan Span);

public abstract record BlockNode(NodeId Id, NodeKind Kind, SourceSpan Span) : Node(Id, Kind, Span);

public abstract record InlineNode(NodeId Id, NodeKind Kind, SourceSpan Span) : Node(Id, Kind, Span);

public sealed record HeadingBlockNode(NodeId Id, SourceSpan Span, int Level, ImmutableArray<InlineNode> Inlines)
    : BlockNode(Id, NodeKind.Heading, Span);

public sealed record ParagraphBlockNode(NodeId Id, SourceSpan Span, ImmutableArray<InlineNode> Inlines)
    : BlockNode(Id, NodeKind.Paragraph, Span);

public sealed record BlockQuoteBlockNode(NodeId Id, SourceSpan Span, ImmutableArray<BlockNode> Blocks)
    : BlockNode(Id, NodeKind.BlockQuote, Span);

public sealed record ListBlockNode(NodeId Id, SourceSpan Span, bool IsOrdered, ImmutableArray<ListItemBlockNode> Items)
    : BlockNode(Id, NodeKind.List, Span);

public sealed record ListItemBlockNode(NodeId Id, SourceSpan Span, bool IsTask, bool? IsChecked, ImmutableArray<BlockNode> Blocks)
    : BlockNode(Id, NodeKind.ListItem, Span);

public sealed record CodeBlockNode(NodeId Id, SourceSpan Span, string? Info, string Code)
    : BlockNode(Id, NodeKind.CodeBlock, Span);

public sealed record TableBlockNode(NodeId Id, SourceSpan Span, ImmutableArray<TableRowBlockNode> Rows)
    : BlockNode(Id, NodeKind.Table, Span);

public sealed record TableRowBlockNode(NodeId Id, SourceSpan Span, ImmutableArray<TableCellBlockNode> Cells)
    : BlockNode(Id, NodeKind.TableRow, Span);

public sealed record TableCellBlockNode(NodeId Id, SourceSpan Span, ImmutableArray<BlockNode> Blocks)
    : BlockNode(Id, NodeKind.TableCell, Span);

public sealed record ThematicBreakBlockNode(NodeId Id, SourceSpan Span)
    : BlockNode(Id, NodeKind.ThematicBreak, Span);

public sealed record TextInlineNode(NodeId Id, SourceSpan Span, string Text)
    : InlineNode(Id, NodeKind.Text, Span);

public sealed record EmphasisInlineNode(NodeId Id, SourceSpan Span, ImmutableArray<InlineNode> Inlines)
    : InlineNode(Id, NodeKind.Emphasis, Span);

public sealed record StrongInlineNode(NodeId Id, SourceSpan Span, ImmutableArray<InlineNode> Inlines)
    : InlineNode(Id, NodeKind.Strong, Span);

public sealed record StrikethroughInlineNode(NodeId Id, SourceSpan Span, ImmutableArray<InlineNode> Inlines)
    : InlineNode(Id, NodeKind.Strikethrough, Span);

public sealed record LinkInlineNode(NodeId Id, SourceSpan Span, string? Url, string? Title, ImmutableArray<InlineNode> Inlines)
    : InlineNode(Id, NodeKind.Link, Span);

public sealed record ImageInlineNode(NodeId Id, SourceSpan Span, string? Url, string? Title, ImmutableArray<InlineNode> AltText)
    : InlineNode(Id, NodeKind.Image, Span);

public sealed record InlineCodeNode(NodeId Id, SourceSpan Span, string Code)
    : InlineNode(Id, NodeKind.InlineCode, Span);

public sealed record LineBreakInlineNode(NodeId Id, SourceSpan Span)
    : InlineNode(Id, NodeKind.LineBreak, Span);
