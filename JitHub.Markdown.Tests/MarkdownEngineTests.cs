using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownEngineTests
{
    [Test]
    public void Parse_returns_document_for_empty_input()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(string.Empty);
        doc.Should().NotBeNull();
    }

    [Test]
    public void Parse_supports_gfm_like_constructs_via_advanced_extensions()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "# Title\n\n- [x] Task\n\n```csharp\nvar x = 1;\n```\n";

        var doc = engine.Parse(markdown);
        doc.ToString().Should().NotBeNullOrWhiteSpace();
    }
}
