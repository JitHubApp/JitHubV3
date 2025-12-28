using Markdig;
using Markdig.Renderers;
using Markdig.Parsers;

namespace JitHub.Markdown;

internal sealed class GitHubEnrichmentsExtension : IMarkdownExtension
{
    private readonly GitHubEnrichmentsOptions _options;

    public GitHubEnrichmentsExtension(GitHubEnrichmentsOptions options)
    {
        _options = options;
    }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (pipeline is null) throw new ArgumentNullException(nameof(pipeline));

        // Run early so we can transform plain text before other parsers consume it.
        pipeline.InlineParsers.Insert(0, new GitHubMentionAndIssueInlineParser(_options));
        pipeline.InlineParsers.Insert(0, new GitHubCommitShaInlineParser(_options));
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
        // No renderer additions.
    }
}
