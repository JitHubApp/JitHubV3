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
        doc.SourceMarkdown.Should().Be(string.Empty);
        doc.Blocks.Should().BeEmpty();
    }

    [Test]
    public void Parse_supports_gfm_baseline_and_preserves_spans()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "# Title\n\nThis is *emph* and **strong** and ~~strike~~.\n\n- [x] Task\n\n> Quote\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n[Uno](https://platform.uno) ![Alt](img.png)\n\n```csharp\nvar x = 1;\n```\n";

        var doc = engine.Parse(markdown);

        doc.Blocks.Should().NotBeEmpty();

        // We don't assert full fidelity in Phase 1, but we do require spans + basic element coverage.
        doc.Blocks.Any(b => b.Kind == NodeKind.Heading).Should().BeTrue();
        doc.Blocks.Any(b => b.Kind == NodeKind.Paragraph).Should().BeTrue();
        doc.Blocks.Any(b => b.Kind == NodeKind.List).Should().BeTrue();
        doc.Blocks.Any(b => b.Kind == NodeKind.BlockQuote).Should().BeTrue();
        doc.Blocks.Any(b => b.Kind == NodeKind.Table).Should().BeTrue();
        doc.Blocks.Any(b => b.Kind == NodeKind.CodeBlock).Should().BeTrue();

        foreach (var block in doc.Blocks)
        {
            block.Span.Start.Should().BeGreaterThanOrEqualTo(0);
            block.Span.EndExclusive.Should().BeLessThanOrEqualTo(markdown.Length);
            block.Span.Length.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Test]
    public void Parse_produces_stable_node_ids_for_identical_input()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "# Title\n\nHello world\n";

        var doc1 = engine.Parse(markdown);
        var doc2 = engine.Parse(markdown);

        doc1.Blocks.Length.Should().Be(doc2.Blocks.Length);
        for (var i = 0; i < doc1.Blocks.Length; i++)
        {
            doc1.Blocks[i].Id.Should().Be(doc2.Blocks[i].Id);
        }
    }

    [Test]
    public void Parse_keeps_most_node_ids_stable_for_small_edits()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc1 = engine.Parse("Para 1\n\nPara 2\n");
        var doc2 = engine.Parse("Para 1\n\nPara 2 changed\n");

        doc1.Blocks.Length.Should().BeGreaterThanOrEqualTo(1);
        doc2.Blocks.Length.Should().BeGreaterThanOrEqualTo(1);

        // The first paragraph should remain stable.
        doc1.Blocks[0].Kind.Should().Be(NodeKind.Paragraph);
        doc2.Blocks[0].Kind.Should().Be(NodeKind.Paragraph);
        doc1.Blocks[0].Id.Should().Be(doc2.Blocks[0].Id);
    }

    [Test]
    public void TextOffsetMap_maps_link_text_selection_to_exact_source_substring()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "See [Uno](https://platform.uno) now.";

        var doc = engine.Parse(markdown);
        var para = doc.Blocks.OfType<ParagraphBlockNode>().First();

        var map = MarkdownTextMapper.BuildForInlines(doc.SourceMarkdown, para.Inlines);
        var start = map.RenderedText.IndexOf("Uno", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var span = map.MapRenderedRangeToSourceSpan(start, "Uno".Length);
        doc.SourceMarkdown.Substring(span.Start, span.Length).Should().Be("Uno");
    }

    [Test]
    public void TextOffsetMap_maps_emphasis_selection_to_inner_content()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "This is *nice*.";

        var doc = engine.Parse(markdown);
        var para = doc.Blocks.OfType<ParagraphBlockNode>().First();

        var map = MarkdownTextMapper.BuildForInlines(doc.SourceMarkdown, para.Inlines);
        var start = map.RenderedText.IndexOf("nice", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);

        var span = map.MapRenderedRangeToSourceSpan(start, "nice".Length);
        doc.SourceMarkdown.Substring(span.Start, span.Length).Should().Be("nice");
    }

    [Test]
    public void SourceMap_can_lookup_entries_for_blocks_and_inlines()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "# H\n\nHello **world**";

        var doc = engine.Parse(markdown);
        doc.Blocks.Should().NotBeEmpty();

        foreach (var block in doc.Blocks)
        {
            doc.SourceMap.TryGet(block.Id, out var entry).Should().BeTrue();
            entry.Kind.Should().Be(block.Kind);
            entry.Span.Should().Be(block.Span);

            foreach (var inline in EnumerateInlines(block))
            {
                doc.SourceMap.TryGet(inline.Id, out var inlineEntry).Should().BeTrue();
                inlineEntry.Kind.Should().Be(inline.Kind);
                inlineEntry.Span.Should().Be(inline.Span);
            }
        }
    }

    [Test]
    public void Spans_cover_expected_markdown_markers_for_tables_and_fenced_code()
    {
        var engine = MarkdownEngine.CreateDefault();
        var markdown = "| A | B |\n|---|---|\n| 1 | 2 |\n\n```txt\nhello\n```\n";

        var doc = engine.Parse(markdown);

        var table = doc.Blocks.First(b => b.Kind == NodeKind.Table);
        var tableSlice = markdown.Substring(table.Span.Start, table.Span.Length);
        tableSlice.Should().Contain("|---|");

        var code = doc.Blocks.First(b => b.Kind == NodeKind.CodeBlock);
        var codeSlice = markdown.Substring(code.Span.Start, code.Span.Length);
        codeSlice.Should().Contain("```");
    }

    private static IEnumerable<InlineNode> EnumerateInlines(BlockNode block)
    {
        return block switch
        {
            HeadingBlockNode h => EnumerateInlines(h.Inlines),
            ParagraphBlockNode p => EnumerateInlines(p.Inlines),
            BlockQuoteBlockNode q => q.Blocks.SelectMany(EnumerateInlines),
            ListBlockNode l => l.Items.SelectMany(EnumerateInlines),
            ListItemBlockNode li => li.Blocks.SelectMany(EnumerateInlines),
            _ => Enumerable.Empty<InlineNode>()
        };
    }

    private static IEnumerable<InlineNode> EnumerateInlines(IEnumerable<InlineNode> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;

            switch (inline)
            {
                case EmphasisInlineNode e:
                    foreach (var c in EnumerateInlines(e.Inlines)) yield return c;
                    break;
                case StrongInlineNode s:
                    foreach (var c in EnumerateInlines(s.Inlines)) yield return c;
                    break;
                case StrikethroughInlineNode st:
                    foreach (var c in EnumerateInlines(st.Inlines)) yield return c;
                    break;
                case LinkInlineNode l:
                    foreach (var c in EnumerateInlines(l.Inlines)) yield return c;
                    break;
                case ImageInlineNode img:
                    foreach (var c in EnumerateInlines(img.AltText)) yield return c;
                    break;
            }
        }
    }
}
