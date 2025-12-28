using System.Collections.Immutable;

namespace JitHub.Markdown;

public sealed class SourceMap
{
    private readonly ImmutableArray<SourceSpanEntry> _entries;
    private readonly Dictionary<NodeId, SourceSpanEntry> _byNodeId;

    public SourceMap(IEnumerable<SourceSpanEntry> entries)
    {
        _entries = entries.ToImmutableArray();
        _byNodeId = _entries
            .GroupBy(e => e.NodeId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public ImmutableArray<SourceSpanEntry> Entries => _entries;

    public bool TryGet(NodeId nodeId, out SourceSpanEntry entry) => _byNodeId.TryGetValue(nodeId, out entry);
}

public readonly record struct SourceSpanEntry(NodeId NodeId, NodeKind Kind, SourceSpan Span);
