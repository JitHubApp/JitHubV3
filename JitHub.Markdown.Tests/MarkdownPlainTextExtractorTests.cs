using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownPlainTextExtractorTests
{
    [Test]
    public void Extract_null_returns_empty_string()
    {
        MarkdownPlainTextExtractor.Extract(null!).Should().BeEmpty();
    }

    [Test]
    public void Extract_empty_returns_empty_string()
    {
        MarkdownPlainTextExtractor.Extract("").Should().BeEmpty();
    }

    [Test]
    public void Extract_flattens_inline_markup()
    {
        var md = "Hello *em* **strong** ~~strike~~ and `code`.";
        MarkdownPlainTextExtractor.Extract(md).Should().Be("Hello em strong strike and code.");
    }

    [Test]
    public void Extract_includes_newlines_between_blocks()
    {
        var md = "# Title\n\nParagraph\n\n```\ncode\n```\n";
        var text = MarkdownPlainTextExtractor.Extract(md).Replace("\r\n", "\n");
        text.Should().MatchRegex("^Title\nParagraph\ncode\n*$");
    }

    [Test]
    public void Extract_list_items_are_joined_with_newlines_without_markers()
    {
        var md = "- one\n- two\n";
        MarkdownPlainTextExtractor.Extract(md).Should().Be("one\ntwo");
    }

    [Test]
    public void Extract_blockquote_flattens_nested_blocks()
    {
        var md = "> First\n>\n> Second\n";
        MarkdownPlainTextExtractor.Extract(md).Should().Be("First\nSecond");
    }

    [Test]
    public void Extract_link_outputs_link_text_only()
    {
        var md = "See [GitHub](https://github.com) now";
        MarkdownPlainTextExtractor.Extract(md).Should().Be("See GitHub now");
    }

    [Test]
    public void Extract_image_outputs_alt_text_only()
    {
        var md = "![Alt text](https://example.com/a.png)";
        MarkdownPlainTextExtractor.Extract(md).Should().Be("Alt text");
    }

    [Test]
    public void Extract_table_outputs_tabs_between_cells_and_newlines_between_rows()
    {
        var md = "| A | B |\n|---|---|\n| C | D |\n";
        MarkdownPlainTextExtractor.Extract(md).Should().Be("A\tB\nC\tD");
    }
}
