namespace JitHub.Markdown;

public sealed record HitRegion(
    NodeId Id,
    NodeKind Kind,
    SourceSpan Span,
    RectF Bounds,
    string? Url);
