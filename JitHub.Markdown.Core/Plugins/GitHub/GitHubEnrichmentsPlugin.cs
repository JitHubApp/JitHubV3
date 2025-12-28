using Markdig;

namespace JitHub.Markdown;

/// <summary>
/// GitHub-specific markdown enrichments: @mentions, #issue/PR references, and commit hashes.
/// Implemented as a Markdig pipeline extension so spans remain source-traceable.
/// </summary>
public sealed class GitHubEnrichmentsPlugin : IMarkdownRenderPlugin
{
    private readonly GitHubEnrichmentsOptions _options;

    public GitHubEnrichmentsPlugin(GitHubEnrichmentsOptions? options = null)
    {
        _options = options ?? new GitHubEnrichmentsOptions();
    }

    public void Register(MarkdownPluginRegistry registry)
    {
        registry.ConfigurePipeline(builder =>
        {
            builder.Extensions.AddIfNotAlready(new GitHubEnrichmentsExtension(_options));
        });
    }
}
