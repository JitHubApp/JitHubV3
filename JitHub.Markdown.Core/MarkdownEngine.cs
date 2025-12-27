using Markdig;
using Markdig.Syntax;

namespace JitHub.Markdown;

public sealed class MarkdownEngine
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownEngine(MarkdownPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public static MarkdownEngine CreateDefault()
    {
        var builder = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions();

        return new MarkdownEngine(builder.Build());
    }

    public MarkdownDocument Parse(string markdown)
    {
        markdown ??= string.Empty;
        return Markdig.Markdown.Parse(markdown, _pipeline);
    }
}
