using System.Collections.Immutable;

namespace JitHub.Markdown;

public sealed class MarkdownLayout
{
    public required float Width { get; init; }

    public required float Height { get; init; }

    public required ImmutableArray<BlockLayout> Blocks { get; init; }

    public ImmutableArray<int> GetVisibleBlockIndices(float viewportTop, float viewportHeight, float overscan = 0)
    {
        var view = new RectF(0, viewportTop - overscan, Width, viewportHeight + (overscan * 2));

        var builder = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < Blocks.Length; i++)
        {
            if (Blocks[i].Bounds.IntersectsWith(view))
            {
                builder.Add(i);
            }
        }

        return builder.ToImmutable();
    }
}

public abstract record BlockLayout(NodeId Id, NodeKind Kind, SourceSpan Span, RectF Bounds, MarkdownBlockStyle Style);

public sealed record ParagraphLayout(
    NodeId Id,
    SourceSpan Span,
    RectF Bounds,
    MarkdownBlockStyle Style,
    ImmutableArray<LineLayout> Lines)
    : BlockLayout(Id, NodeKind.Paragraph, Span, Bounds, Style);

public sealed record HeadingLayout(
    NodeId Id,
    SourceSpan Span,
    RectF Bounds,
    MarkdownBlockStyle Style,
    int Level,
    ImmutableArray<LineLayout> Lines)
    : BlockLayout(Id, NodeKind.Heading, Span, Bounds, Style);

public sealed record CodeBlockLayout(
    NodeId Id,
    SourceSpan Span,
    RectF Bounds,
    MarkdownBlockStyle Style,
    ImmutableArray<LineLayout> Lines)
    : BlockLayout(Id, NodeKind.CodeBlock, Span, Bounds, Style);

public sealed record BlockQuoteLayout(
    NodeId Id,
    SourceSpan Span,
    RectF Bounds,
    MarkdownBlockStyle Style,
    ImmutableArray<BlockLayout> Blocks)
    : BlockLayout(Id, NodeKind.BlockQuote, Span, Bounds, Style);

public sealed record ThematicBreakLayout(
    NodeId Id,
    SourceSpan Span,
    RectF Bounds,
    MarkdownBlockStyle Style)
    : BlockLayout(Id, NodeKind.ThematicBreak, Span, Bounds, Style);

public sealed record UnknownBlockLayout(
    NodeId Id,
    NodeKind Kind,
    SourceSpan Span,
    RectF Bounds,
    MarkdownBlockStyle Style)
    : BlockLayout(Id, Kind, Span, Bounds, Style);

public sealed record LineLayout(float Y, float Height, ImmutableArray<InlineRunLayout> Runs);

public sealed record InlineRunLayout(
    NodeId Id,
    NodeKind Kind,
    SourceSpan Span,
    RectF Bounds,
    MarkdownTextStyle Style,
    string Text,
    string? Url,
    bool IsStrikethrough,
    bool IsCodeBlockLine);
