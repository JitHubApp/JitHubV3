using JitHub.Markdown;
using System.Collections.Immutable;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownPointerInteractionEdgeTests
{
    [Test]
    public void CancelPointer_clears_pending_link_activation()
    {
        var (hit, x, y) = CreateLinkHit();

        var sm = new SelectionPointerInteraction();
        sm.OnPointerDown(hit, x: x, y: y, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false))
            .ActivateLinkUrl.Should().BeNull();

        sm.CancelPointer();

        var up = sm.OnPointerUp(hit, selectionEnabled: true);
        up.ActivateLinkUrl.Should().BeNull();
    }

    [Test]
    public void PointerDown_on_link_outside_bounds_starts_selection_when_enabled()
    {
        var (hit, _, y) = CreateLinkHit();
        var xOutside = hit.Run.Bounds.Right + 10;

        var sm = new SelectionPointerInteraction();
        var down = sm.OnPointerDown(hit, x: xOutside, y: y, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false));

        down.SelectionChanged.Should().BeTrue("outside link bounds should not arm activation");
        down.Selection.Should().NotBeNull();
        down.ActivateLinkUrl.Should().BeNull();
    }

    [Test]
    public void Click_on_link_activates_when_no_drag_occurs()
    {
        var (hit, x, y) = CreateLinkHit();

        var sm = new SelectionPointerInteraction();
        sm.OnPointerDown(hit, x: x, y: y, selectionEnabled: true, modifiers: new PointerModifiers(Shift: false))
            .SelectionChanged.Should().BeFalse();

        // Small movement under drag threshold.
        sm.OnPointerMove(hit, x: x + 1, y: y + 1, selectionEnabled: true)
            .SelectionChanged.Should().BeFalse();

        var up = sm.OnPointerUp(hit, selectionEnabled: true);
        up.ActivateLinkUrl.Should().Be(hit.Run.Url);
    }

    private static (MarkdownHitTestResult hit, float x, float y) CreateLinkHit()
    {
        var run = new InlineRunLayout(
            Id: new NodeId(1),
            Kind: NodeKind.Link,
            Span: default,
            Bounds: new RectF(10, 0, 40, 16),
            Style: MarkdownTheme.Light.Typography.Link,
            Text: "here",
            Url: "https://example.com",
            IsStrikethrough: false,
            IsCodeBlockLine: false,
            GlyphX: ImmutableArray.Create(10f, 20f, 30f, 40f, 50f));

        var line = new LineLayout(Y: 0, Height: 16, Runs: ImmutableArray.Create(run));

        var hit = new MarkdownHitTestResult(
            LineIndex: 0,
            RunIndex: 0,
            Run: run,
            Line: line,
            TextOffset: 0,
            CaretX: MarkdownHitTester.GetCaretX(run, 0));

        var x = (run.Bounds.X + run.Bounds.Right) / 2f;
        var y = line.Y + (line.Height / 2f);
        return (hit, x, y);
    }
}
