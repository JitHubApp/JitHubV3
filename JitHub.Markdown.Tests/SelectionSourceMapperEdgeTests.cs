using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class SelectionSourceMapperEdgeTests
{
    [Test]
    public void TryMapToSource_throws_for_null_arguments()
    {
        var doc = new MarkdownDocumentModel("x", ImmutableArray<BlockNode>.Empty, new SourceMap([]));
        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(0, 1),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "x",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        Assert.Throws<ArgumentNullException>(() => SelectionSourceMapper.TryMapToSource(null!, doc, range, out _));
        Assert.Throws<ArgumentNullException>(() => SelectionSourceMapper.TryMapToSource("x", null!, range, out _));

        Assert.Throws<ArgumentNullException>(() => SelectionSourceMapper.TryMapToSource(null!, doc, range, mappers: Array.Empty<ISelectionSourceIndexMapper>(), out _));
        Assert.Throws<ArgumentNullException>(() => SelectionSourceMapper.TryMapToSource("x", null!, range, mappers: Array.Empty<ISelectionSourceIndexMapper>(), out _));
    }

    [Test]
    public void Image_runs_map_to_span_start()
    {
        var markdown = "![alt](url)";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Image,
            Span: new SourceSpan(3, 10),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "alt",
            Url: "url",
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 2);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(3);
        selection.EndExclusive.Should().Be(3);
    }

    [Test]
    public void Inline_code_maps_to_inner_content_not_backticks()
    {
        var markdown = "`hi`";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.InlineCode,
            Span: new SourceSpan(0, markdown.Length),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "hi",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 1);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(2); // `h[i]`
        selection.EndExclusive.Should().Be(2);
    }

    [Test]
    public void Inline_code_falls_back_to_span_start_when_content_not_found()
    {
        var markdown = "`hi`";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.InlineCode,
            Span: new SourceSpan(0, markdown.Length),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "bye",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 1);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(1);
        selection.EndExclusive.Should().Be(1);
    }

    [Test]
    public void Inline_code_with_invalid_span_falls_back_to_span_start_mapping()
    {
        var markdown = "`hi`";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.InlineCode,
            Span: new SourceSpan(0, markdown.Length + 10), // invalid end
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "hi",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 1);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(1);
    }

    [Test]
    public void Code_block_line_maps_into_code_content_when_node_is_found()
    {
        var code = "abc\ndef";
        var markdown = $"```csharp\n{code}\n```";

        var span = new SourceSpan(0, markdown.Length);
        var id = new NodeId(123);

        var codeBlock = new CodeBlockNode(id, span, Info: "csharp", Code: code);
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(codeBlock), new SourceMap([]));

        // Second line: "def" starts after "abc\n" => nodeTextOffset = 4
        var run = new InlineRunLayout(
            Id: id,
            Kind: NodeKind.InlineCode,
            Span: span,
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "def",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 4,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 1);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();

        var contentStart = markdown.IndexOf(code, StringComparison.Ordinal);
        contentStart.Should().BeGreaterThan(0);

        selection.Start.Should().Be(contentStart + 4 + 1);
        selection.EndExclusive.Should().Be(contentStart + 4 + 1);
    }

    [Test]
    public void Code_block_line_falls_back_to_span_start_when_node_is_missing()
    {
        var markdown = "irrelevant";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: new NodeId(999),
            Kind: NodeKind.InlineCode,
            Span: new SourceSpan(5, 7),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "x",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(5);
    }

    [Test]
    public void Code_block_line_falls_back_to_codeblock_span_start_when_content_start_cannot_be_found()
    {
        var markdown = "```csharp\nabc\n```";
        var span = new SourceSpan(0, markdown.Length);
        var id = new NodeId(456);

        // Code is not present inside the markdown span.
        var codeBlock = new CodeBlockNode(id, span, Info: "csharp", Code: "xyz");
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(codeBlock), new SourceMap([]));

        var run = new InlineRunLayout(
            Id: id,
            Kind: NodeKind.InlineCode,
            Span: span,
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "abc",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(0);
    }

    [Test]
    public void Code_block_line_falls_back_when_codeblock_span_is_invalid()
    {
        var markdown = "AAA";
        var invalidSpan = new SourceSpan(0, markdown.Length + 5);
        var id = new NodeId(777);

        var codeBlock = new CodeBlockNode(id, invalidSpan, Info: null, Code: "AAA");
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(codeBlock), new SourceMap([]));

        var run = new InlineRunLayout(
            Id: id,
            Kind: NodeKind.InlineCode,
            Span: invalidSpan,
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "AAA",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(0);
    }

    [Test]
    public void Code_block_lookup_traverses_nested_blocks()
    {
        var markdown = "AAA BBB CCC";
        var span = new SourceSpan(0, markdown.Length);

        var codeInQuote = new CodeBlockNode(new NodeId(1), span, Info: null, Code: "AAA");
        var quote = new BlockQuoteBlockNode(new NodeId(2), span, ImmutableArray.Create<BlockNode>(codeInQuote));

        var codeInList = new CodeBlockNode(new NodeId(3), span, Info: null, Code: "BBB");
        var li = new ListItemBlockNode(new NodeId(4), span, IsTask: false, IsChecked: null, ImmutableArray.Create<BlockNode>(codeInList));
        var list = new ListBlockNode(new NodeId(5), span, IsOrdered: false, ImmutableArray.Create(li));

        var codeInTable = new CodeBlockNode(new NodeId(6), span, Info: null, Code: "CCC");
        var cell = new TableCellBlockNode(new NodeId(7), span, ImmutableArray.Create<BlockNode>(codeInTable));
        var row = new TableRowBlockNode(new NodeId(8), span, ImmutableArray.Create(cell));
        var table = new TableBlockNode(new NodeId(9), span, ImmutableArray.Create(row));

        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(quote, list, table), new SourceMap([]));

        MapAndAssert(codeInQuote.Id, "AAA");
        MapAndAssert(codeInList.Id, "BBB");
        MapAndAssert(codeInTable.Id, "CCC");

        void MapAndAssert(NodeId codeBlockId, string code)
        {
            var run = new InlineRunLayout(
                Id: codeBlockId,
                Kind: NodeKind.InlineCode,
                Span: span,
                Bounds: default,
                Style: MarkdownTheme.Light.Typography.InlineCode,
                Text: code,
                Url: null,
                IsStrikethrough: false,
                IsCodeBlockLine: true,
                GlyphX: ImmutableArray<float>.Empty,
                NodeTextOffset: 0,
                IsRightToLeft: false);

            var range = CreateRange(run, textOffset: 0);
            SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
            selection.Start.Should().Be(markdown.IndexOf(code, StringComparison.Ordinal));
        }
    }

    [Test]
    public void Code_block_lookup_handles_table_cell_block_nodes_directly()
    {
        var markdown = "prefix CCC suffix";
        var span = new SourceSpan(0, markdown.Length);

        var codeInCell = new CodeBlockNode(new NodeId(10), span, Info: null, Code: "CCC");
        var cellRoot = new TableCellBlockNode(new NodeId(11), span, ImmutableArray.Create<BlockNode>(codeInCell));

        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(cellRoot), new SourceMap([]));

        var run = new InlineRunLayout(
            Id: codeInCell.Id,
            Kind: NodeKind.InlineCode,
            Span: span,
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "CCC",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(markdown.IndexOf("CCC", StringComparison.Ordinal));
    }

    [Test]
    public void Code_block_lookup_handles_list_item_block_nodes_directly()
    {
        var markdown = "prefix BBB suffix";
        var span = new SourceSpan(0, markdown.Length);

        var codeInItem = new CodeBlockNode(new NodeId(20), span, Info: null, Code: "BBB");
        var listItemRoot = new ListItemBlockNode(
            new NodeId(21),
            span,
            IsTask: false,
            IsChecked: null,
            ImmutableArray.Create<BlockNode>(codeInItem));

        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(listItemRoot), new SourceMap([]));

        var run = new InlineRunLayout(
            Id: codeInItem.Id,
            Kind: NodeKind.InlineCode,
            Span: span,
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "BBB",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(markdown.IndexOf("BBB", StringComparison.Ordinal));
    }

    [Test]
    public void Non_mapper_path_swaps_indices_when_mapping_inverts_source_order()
    {
        var markdown = "0123456789";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        // Ensure normalization picks start by LineIndex/RunIndex, even if spans are inverted.
        var runA = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(8, 10),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "89",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var runB = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(0, 2),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "01",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var line0 = new LineLayout(0, 0, ImmutableArray.Create(runA));
        var line1 = new LineLayout(0, 0, ImmutableArray.Create(runB));

        var anchor = new MarkdownHitTestResult(LineIndex: 0, RunIndex: 0, Run: runA, Line: line0, TextOffset: 0, CaretX: 0);
        var active = new MarkdownHitTestResult(LineIndex: 1, RunIndex: 0, Run: runB, Line: line1, TextOffset: 0, CaretX: 0);
        var range = new SelectionRange(anchor, active);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(0);
        selection.EndExclusive.Should().Be(8);
    }

    [Test]
    public void Code_block_lookup_handles_unknown_block_node_types()
    {
        var markdown = "```\ncode\n```";
        var span = new SourceSpan(0, markdown.Length);
        var id = new NodeId(42);

        var doc = new MarkdownDocumentModel(markdown, ImmutableArray.Create<BlockNode>(new CustomBlockNode(new NodeId(99), span)), new SourceMap([]));

        var run = new InlineRunLayout(
            Id: id,
            Kind: NodeKind.InlineCode,
            Span: new SourceSpan(5, 9),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.InlineCode,
            Text: "code",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: true,
            GlyphX: ImmutableArray<float>.Empty,
            NodeTextOffset: 0,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 0);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(run.Span.Start);
    }

    [Test]
    public void Negative_span_start_is_clamped_to_zero()
    {
        var markdown = "Hello";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(-10, -5),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "Hello",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 3);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, out var selection).Should().BeTrue();
        selection.Start.Should().Be(0);
    }

    [Test]
    public void Mapped_indices_are_clamped_and_swapped_when_needed()
    {
        var markdown = "Hello";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(0, 5),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "Hello",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var line = new LineLayout(0, 0, ImmutableArray.Create(run));
        var anchor = new MarkdownHitTestResult(0, 0, run, line, 0, 0);
        var active = anchor with { TextOffset = 1 };
        var range = new SelectionRange(anchor, active);

        // Make start map past end so the mapper-produced indices need swapping.
        // Also return values outside the document bounds to cover clamping.
        var mappers = new ISelectionSourceIndexMapper[] { new OffsetDependentMapper() };

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, mappers, out var selection).Should().BeTrue();
        selection.Start.Should().Be(0);
        selection.EndExclusive.Should().Be(markdown.Length);
    }

    [Test]
    public void Default_mapping_handles_null_run_text_without_throwing()
    {
        var markdown = "Hello";
        var doc = new MarkdownDocumentModel(markdown, ImmutableArray<BlockNode>.Empty, new SourceMap([]));

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(2, 4),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: null!,
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var range = CreateRange(run, textOffset: 999);

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, mappers: Array.Empty<ISelectionSourceIndexMapper>(), out var selection)
            .Should().BeTrue();
        selection.Start.Should().Be(2);
    }

    private static SelectionRange CreateRange(InlineRunLayout run, int textOffset)
    {
        var line = new LineLayout(0, 0, ImmutableArray.Create(run));
        var caret = new MarkdownHitTestResult(0, 0, run, line, textOffset, 0);
        return new SelectionRange(caret, caret);
    }

    private sealed class OffsetDependentMapper : ISelectionSourceIndexMapper
    {
        public bool TryMapCaretToSourceIndex(
            string sourceMarkdown,
            MarkdownDocumentModel document,
            InlineRunLayout run,
            int caretTextOffset,
            out int sourceIndex)
        {
            // For the start caret (offset 0) return > length; for end caret (offset 1) return < 0.
            sourceIndex = caretTextOffset == 0 ? 999 : -50;
            return true;
        }
    }

    private sealed record CustomBlockNode(NodeId Id, SourceSpan Span)
        : BlockNode(Id, NodeKind.Paragraph, Span);
}
