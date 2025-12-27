using System.Collections.Immutable;

namespace JitHub.Markdown;

public sealed class MarkdownDocumentModel
{
    public MarkdownDocumentModel(
        string sourceMarkdown,
        ImmutableArray<BlockNode> blocks,
        SourceMap sourceMap)
    {
        SourceMarkdown = sourceMarkdown;
        Blocks = blocks;
        SourceMap = sourceMap;
    }

    public string SourceMarkdown { get; }

    public ImmutableArray<BlockNode> Blocks { get; }

    public SourceMap SourceMap { get; }
}
