using Markdig;
using Markdig.Syntax;

namespace JitHub.Markdown;

public sealed class MarkdownEngine
{
    private readonly MarkdownPipeline _pipeline;
    private readonly DocumentBuilder _builder;

    public MarkdownEngine(MarkdownPipeline pipeline, MarkdownParserOptions options)
    {
        _pipeline = pipeline;
        Options = options;
        _builder = new DocumentBuilder(options.AllowHtml);
    }

    public MarkdownParserOptions Options { get; }

    public static MarkdownEngine CreateDefault()
    {
        var options = new MarkdownParserOptions
        {
            ConfigurePipeline = builder => builder.UseAdvancedExtensions()
        };

        return Create(options);
    }

    public static MarkdownEngine Create(MarkdownParserOptions options)
    {
        var builder = new MarkdownPipelineBuilder();
        options.ConfigurePipeline?.Invoke(builder);

        return new MarkdownEngine(builder.Build(), options);
    }

    public MarkdownDocumentModel Parse(string markdown)
    {
        markdown ??= string.Empty;
        MarkdownDocument ast = Markdig.Markdown.Parse(markdown, _pipeline);
        return _builder.Build(markdown, ast);
    }
}
