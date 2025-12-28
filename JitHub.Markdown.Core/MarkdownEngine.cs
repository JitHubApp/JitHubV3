using Markdig;
using Markdig.Syntax;

namespace JitHub.Markdown;

public sealed class MarkdownEngine
{
    private readonly MarkdownPipeline _pipeline;
    private readonly DocumentBuilder _builder;

    public MarkdownEngine(MarkdownPipeline pipeline, MarkdownParserOptions options, MarkdownPluginRegistry plugins)
    {
        _pipeline = pipeline;
        Options = options;
        Plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
        _builder = new DocumentBuilder(options.AllowHtml);
    }

    public MarkdownParserOptions Options { get; }

    public MarkdownPluginRegistry Plugins { get; }

    public static MarkdownEngine CreateDefault()
    {
        var options = new MarkdownParserOptions
        {
            ConfigurePipeline = builder => builder.UseAdvancedExtensions()
        };

        return Create(options);
    }

    public static MarkdownEngine Create(MarkdownParserOptions options, params IMarkdownRenderPlugin[] plugins)
    {
        var registry = new MarkdownPluginRegistry();
        if (plugins is not null)
        {
            for (var i = 0; i < plugins.Length; i++)
            {
                plugins[i]?.Register(registry);
            }
        }

        var builder = new MarkdownPipelineBuilder();
        options.ConfigurePipeline?.Invoke(builder);
        for (var i = 0; i < registry.PipelineConfigurators.Count; i++)
        {
            registry.PipelineConfigurators[i](builder);
        }

        return new MarkdownEngine(builder.Build(), options, registry);
    }

    public MarkdownDocumentModel Parse(string markdown)
    {
        markdown ??= string.Empty;
        MarkdownDocument ast = Markdig.Markdown.Parse(markdown, _pipeline);
        return _builder.Build(markdown, ast);
    }
}
