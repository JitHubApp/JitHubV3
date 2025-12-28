using Markdig;

namespace JitHub.Markdown;

public sealed class MarkdownParserOptions
{
    /// <summary>
    /// When false (default), HTML is parsed by Markdig but is ignored by our model builder.
    /// </summary>
    public bool AllowHtml { get; init; } = false;

    /// <summary>
    /// Optional hook to configure the Markdig pipeline.
    /// </summary>
    public Action<MarkdownPipelineBuilder>? ConfigurePipeline { get; init; }

    public static MarkdownParserOptions CreateDefault() => new();
}
