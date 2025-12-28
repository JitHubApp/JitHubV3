using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class SelectionSourceMapperPluginTests
{
    [Test]
    public void Custom_mappers_are_applied_in_order_first_match_wins()
    {
        var markdown = "Hello";
        var doc = MarkdownEngine.CreateDefault().Parse(markdown);

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

        var anchor = new MarkdownHitTestResult(
            LineIndex: 0,
            RunIndex: 0,
            Run: run,
            Line: line,
            TextOffset: 0,
            CaretX: 0);

        var active = anchor with { TextOffset = 1 };
        var range = new SelectionRange(anchor, active);

        var mappers = new ISelectionSourceIndexMapper[]
        {
            new ConstantMapper(3),
            new ConstantMapper(4),
        };

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, mappers, out var selection).Should().BeTrue();
        selection.Start.Should().Be(3);
        selection.EndExclusive.Should().Be(3);
    }

    [Test]
    public void Null_mappers_are_skipped()
    {
        var markdown = "Hello";
        var doc = MarkdownEngine.CreateDefault().Parse(markdown);

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

        var mappers = new ISelectionSourceIndexMapper?[]
        {
            null,
            new ConstantMapper(2),
        };

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, mappers!, out var selection).Should().BeTrue();
        selection.Start.Should().Be(2);
        selection.EndExclusive.Should().Be(2);
    }

    [Test]
    public void When_no_custom_mapper_matches_falls_back_to_span_start_plus_offset()
    {
        var markdown = "Hello";
        var doc = MarkdownEngine.CreateDefault().Parse(markdown);

        var run = new InlineRunLayout(
            Id: default,
            Kind: NodeKind.Text,
            Span: new SourceSpan(2, 5),
            Bounds: default,
            Style: MarkdownTheme.Light.Typography.Paragraph,
            Text: "llo",
            Url: null,
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray<float>.Empty,
            IsRightToLeft: false);

        var line = new LineLayout(0, 0, ImmutableArray.Create(run));
        var anchor = new MarkdownHitTestResult(0, 0, run, line, 1, 0); // within "llo"
        var range = new SelectionRange(anchor, anchor);

        var mappers = new ISelectionSourceIndexMapper[] { new NeverMapper() };

        SelectionSourceMapper.TryMapToSource(markdown, doc, range, mappers, out var selection).Should().BeTrue();
        selection.Start.Should().Be(3);
        selection.EndExclusive.Should().Be(3);
    }

    private sealed class ConstantMapper : ISelectionSourceIndexMapper
    {
        private readonly int _value;

        public ConstantMapper(int value) => _value = value;

        public bool TryMapCaretToSourceIndex(
            string sourceMarkdown,
            MarkdownDocumentModel document,
            InlineRunLayout run,
            int caretTextOffset,
            out int sourceIndex)
        {
            sourceIndex = _value;
            return true;
        }
    }

    private sealed class NeverMapper : ISelectionSourceIndexMapper
    {
        public bool TryMapCaretToSourceIndex(
            string sourceMarkdown,
            MarkdownDocumentModel document,
            InlineRunLayout run,
            int caretTextOffset,
            out int sourceIndex)
        {
            sourceIndex = -1;
            return false;
        }
    }
}
