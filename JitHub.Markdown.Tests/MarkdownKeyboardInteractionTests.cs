using FluentAssertions;
using NUnit.Framework;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownKeyboardInteractionTests
{
    [Test]
    public void Arrow_right_moves_caret_when_selection_enabled()
    {
        var markdown = "Hello world";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var helloRunIndex = line.Runs.ToList().FindIndex(r => r.Text == "Hello");
        helloRunIndex.Should().BeGreaterThanOrEqualTo(0);

        var helloRun = line.Runs[helloRunIndex];
        var caret = new MarkdownHitTestResult(0, helloRunIndex, helloRun, line, TextOffset: 0, CaretX: MarkdownHitTester.GetCaretX(helloRun, 0));

        var kb = new SelectionKeyboardInteraction { Selection = new SelectionRange(caret, caret) };
        var result = kb.OnKeyCommand(layout, MarkdownKeyCommand.Right, selectionEnabled: true, shift: false);

        result.Handled.Should().BeTrue();
        result.SelectionChanged.Should().BeTrue();
        result.Selection.Should().NotBeNull();
        result.Selection!.Value.Active.TextOffset.Should().Be(1);
    }

    [Test]
    public void Arrow_keys_unhandled_when_selection_disabled()
    {
        var markdown = "Hello";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var kb = new SelectionKeyboardInteraction();
        var result = kb.OnKeyCommand(layout, MarkdownKeyCommand.Right, selectionEnabled: false, shift: false);

        result.Handled.Should().BeFalse();
    }

    [Test]
    public void Tab_focuses_first_link_and_enter_activates_it()
    {
        var markdown = "See [here](https://example.com) and [there](https://example.org).";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var kb = new SelectionKeyboardInteraction();

        var tab1 = kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false);
        tab1.Handled.Should().BeTrue();
        kb.FocusedLink.Should().NotBeNull();
        kb.FocusedLink!.Value.Url.Should().Be("https://example.com");

        var enter = kb.OnKeyCommand(layout, MarkdownKeyCommand.Enter, selectionEnabled: true, shift: false);
        enter.Handled.Should().BeTrue();
        enter.ActivateLinkUrl.Should().Be("https://example.com");

        var tab2 = kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false);
        tab2.Handled.Should().BeTrue();
        kb.FocusedLink.Should().NotBeNull();
        kb.FocusedLink!.Value.Url.Should().Be("https://example.org");
    }

    [Test]
    public void Shift_tab_moves_link_focus_backwards()
    {
        var markdown = "See [here](https://example.com) and [there](https://example.org).";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var kb = new SelectionKeyboardInteraction();

        kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false).Handled.Should().BeTrue();
        kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false).Handled.Should().BeTrue();
        kb.FocusedLink.Should().NotBeNull();
        kb.FocusedLink!.Value.Url.Should().Be("https://example.org");

        var back = kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: true);
        back.Handled.Should().BeTrue();
        kb.FocusedLink.Should().NotBeNull();
        kb.FocusedLink!.Value.Url.Should().Be("https://example.com");
    }

    [Test]
    public void Tab_returns_unhandled_when_no_links_present()
    {
        var markdown = "No links here.";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var kb = new SelectionKeyboardInteraction();
        var tab = kb.OnKeyCommand(layout, MarkdownKeyCommand.Tab, selectionEnabled: true, shift: false);

        tab.Handled.Should().BeFalse();
    }
}
