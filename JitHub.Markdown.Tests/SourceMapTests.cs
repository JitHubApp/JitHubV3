using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class SourceMapTests
{
    [Test]
    public void TryGet_returns_false_for_unknown_node()
    {
        var map = new SourceMap(Array.Empty<SourceSpanEntry>());
        map.TryGet(new NodeId(123), out _).Should().BeFalse();
    }

    [Test]
    public void Duplicate_nodeid_keeps_first_entry()
    {
        var id = new NodeId(1);
        var first = new SourceSpanEntry(id, NodeKind.Text, new SourceSpan(0, 1));
        var second = new SourceSpanEntry(id, NodeKind.Text, new SourceSpan(10, 20));

        var map = new SourceMap(new[] { first, second });
        map.TryGet(id, out var found).Should().BeTrue();
        found.Should().Be(first);
    }
}
