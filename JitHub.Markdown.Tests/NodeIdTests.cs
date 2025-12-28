using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class NodeIdTests
{
    [Test]
    public void ToString_formats_as_16_hex_digits()
    {
        new NodeId(1).ToString().Should().Be("0000000000000001");
        new NodeId(0xABCD).ToString().Should().Be("000000000000ABCD");
    }

    [Test]
    public void Create_is_deterministic_for_same_inputs()
    {
        var a = NodeId.Create(NodeKind.Paragraph, new SourceSpan(0, 10), ordinal: 1, parent: new NodeId(0));
        var b = NodeId.Create(NodeKind.Paragraph, new SourceSpan(0, 10), ordinal: 1, parent: new NodeId(0));
        a.Should().Be(b);
    }

    [Test]
    public void Create_changes_when_any_input_changes()
    {
        var baseId = NodeId.Create(NodeKind.Paragraph, new SourceSpan(0, 10), ordinal: 1, parent: new NodeId(0));

        NodeId.Create(NodeKind.Heading, new SourceSpan(0, 10), ordinal: 1, parent: new NodeId(0)).Should().NotBe(baseId);
        NodeId.Create(NodeKind.Paragraph, new SourceSpan(0, 11), ordinal: 1, parent: new NodeId(0)).Should().NotBe(baseId);
        NodeId.Create(NodeKind.Paragraph, new SourceSpan(1, 10), ordinal: 1, parent: new NodeId(0)).Should().NotBe(baseId);
        NodeId.Create(NodeKind.Paragraph, new SourceSpan(0, 10), ordinal: 2, parent: new NodeId(0)).Should().NotBe(baseId);
        NodeId.Create(NodeKind.Paragraph, new SourceSpan(0, 10), ordinal: 1, parent: new NodeId(123)).Should().NotBe(baseId);
    }
}
