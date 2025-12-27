namespace JitHub.Markdown;

public sealed class MarkdownSpanMappingPolicy
{
    /// <summary>
    /// Controls how emphasis/strong/strikethrough selections map back to source spans.
    /// Phase 1 default: map to the inner content (excluding markup markers).
    /// </summary>
    public InlineMarkupSpanBehavior EmphasisBehavior { get; init; } = InlineMarkupSpanBehavior.InnerContent;
}

public enum InlineMarkupSpanBehavior
{
    /// <summary>
    /// Prefer mapping to the human-visible content range (excluding markup markers).
    /// </summary>
    InnerContent = 0,

    /// <summary>
    /// Prefer mapping to the full node span (including markup markers).
    /// </summary>
    NodeSpan = 1,
}
