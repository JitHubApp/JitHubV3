using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownPointerInteractionTests
{
    [Test]
    public void Click_on_link_activates_link_without_forcing_selection()
    {
        var markdown = "Click [here](https://example.com).";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var runIndex = line.Runs.ToList().FindIndex(r => r.Kind == NodeKind.Link && r.Url == "https://example.com" && r.Text == "here");
        runIndex.Should().BeGreaterThanOrEqualTo(0);

        var run = line.Runs[runIndex];
        var hit = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 1, CaretX: MarkdownHitTester.GetCaretX(run, 1));

        var sm = new SelectionPointerInteraction();
        sm.OnPointerDown(hit, x: 10, y: 10, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false))
            .SelectionChanged.Should().BeFalse();

        var up = sm.OnPointerUp(hit, selectionEnabled: true);
        up.ActivateLinkUrl.Should().Be("https://example.com");
    }

    [Test]
    public void Drag_on_link_starts_selection_instead_of_activation()
    {
        var markdown = "Click [here](https://example.com).";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var runIndex = line.Runs.ToList().FindIndex(r => r.Kind == NodeKind.Link && r.Url == "https://example.com" && r.Text == "here");
        runIndex.Should().BeGreaterThanOrEqualTo(0);

        var run = line.Runs[runIndex];
        var downHit = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 0, CaretX: MarkdownHitTester.GetCaretX(run, 0));
        var moveHit = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 4, CaretX: MarkdownHitTester.GetCaretX(run, 4));

        var sm = new SelectionPointerInteraction();
        sm.OnPointerDown(downHit, x: 10, y: 10, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false));

        // Move far enough to exceed drag threshold.
        var move = sm.OnPointerMove(moveHit, x: 30, y: 10, selectionEnabled: true);
        move.SelectionChanged.Should().BeTrue();
        move.Selection.Should().NotBeNull();
        move.ActivateLinkUrl.Should().BeNull();

        var up = sm.OnPointerUp(moveHit, selectionEnabled: true);
        up.ActivateLinkUrl.Should().BeNull();
        up.Selection.Should().NotBeNull();
    }

    [Test]
    public void Drag_on_link_does_not_activate_when_selection_disabled()
    {
        var markdown = "Click [here](https://example.com).";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var runIndex = line.Runs.ToList().FindIndex(r => r.Kind == NodeKind.Link && r.Url == "https://example.com" && r.Text == "here");
        runIndex.Should().BeGreaterThanOrEqualTo(0);

        var run = line.Runs[runIndex];
        var downHit = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 0, CaretX: MarkdownHitTester.GetCaretX(run, 0));
        var moveHit = new MarkdownHitTestResult(0, runIndex, run, line, TextOffset: 4, CaretX: MarkdownHitTester.GetCaretX(run, 4));

        var sm = new SelectionPointerInteraction();
        sm.OnPointerDown(downHit, x: 10, y: 10, selectionEnabled: false, modifiers: new PointerModifiers(Shift: false));

        // Move far enough to exceed drag threshold.
        sm.OnPointerMove(moveHit, x: 30, y: 10, selectionEnabled: false);

        var up = sm.OnPointerUp(moveHit, selectionEnabled: false);
        up.ActivateLinkUrl.Should().BeNull();
    }

    [Test]
    public void Shift_click_extends_existing_selection_anchor()
    {
        var markdown = "Hello world";
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var helloRunIndex = line.Runs.ToList().FindIndex(r => r.Text == "Hello");
        var worldRunIndex = line.Runs.ToList().FindIndex(r => r.Text == "world");
        helloRunIndex.Should().BeGreaterThanOrEqualTo(0);
        worldRunIndex.Should().BeGreaterThanOrEqualTo(0);

        var helloRun = line.Runs[helloRunIndex];
        var worldRun = line.Runs[worldRunIndex];

        var helloHit = new MarkdownHitTestResult(0, helloRunIndex, helloRun, line, TextOffset: 0, CaretX: MarkdownHitTester.GetCaretX(helloRun, 0));
        var worldHit = new MarkdownHitTestResult(0, worldRunIndex, worldRun, line, TextOffset: 5, CaretX: MarkdownHitTester.GetCaretX(worldRun, 5));

        var sm = new SelectionPointerInteraction();
        sm.OnPointerDown(helloHit, x: 10, y: 10, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false));
        sm.OnPointerUp(helloHit, selectionEnabled: true);

        var shifted = sm.OnPointerDown(worldHit, x: 100, y: 10, selectionEnabled: true, modifiers: new PointerModifiers(Shift: true));
        shifted.SelectionChanged.Should().BeTrue();
        shifted.Selection.Should().NotBeNull();
        shifted.Selection!.Value.Anchor.Run.Text.Should().Be("Hello");
        shifted.Selection!.Value.Active.Run.Text.Should().Be("world");
    }
}
