using FluentAssertions;
using JitHub.Dashboard.Layouts;

namespace JitHub.Dashboard.Layouts.Tests;

public sealed class CardDeckLayoutMathTests
{
    [Test]
    public void Auto_mode_uses_grid_at_or_above_threshold()
    {
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Auto,
            ModeWidthThreshold = 900,
            CardMinWidth = 300,
            CardHeight = 200,
            Spacing = 16,
            MaxColumns = 3,
        };

        var items = CardDeckLayoutMath.Compute(new LayoutSize(900, 800), itemCount: 5, settings);

        items.Should().HaveCount(5);
        items.Select(i => i.Transform).All(t => t.RotationDegrees == 0 && t.TranslateY == 0).Should().BeTrue();
    }

    [Test]
    public void Auto_mode_uses_deck_below_threshold()
    {
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Auto,
            ModeWidthThreshold = 900,
            CardMinWidth = 300,
            CardHeight = 200,
            DeckMaxVisibleCount = 4,
            DeckOffsetY = 10,
            DeckAngleStepDegrees = 2,
            DeckScaleStep = 0,
        };

        var items = CardDeckLayoutMath.Compute(new LayoutSize(899, 800), itemCount: 6, settings);

        items.Should().HaveCount(6);
        items[0].Transform.TranslateY.Should().Be(0);
        items[1].Transform.TranslateY.Should().Be(10);
        items[2].Transform.TranslateY.Should().Be(20);
        items[3].Transform.TranslateY.Should().Be(30);
        // clamped
        items[4].Transform.TranslateY.Should().Be(30);
        items[5].Transform.TranslateY.Should().Be(30);
    }

    [Test]
    public void Grid_layout_centers_gutter_and_uses_fixed_card_size()
    {
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Grid,
            CardMinWidth = 200,
            CardHeight = 100,
            Spacing = 20,
            MaxColumns = 3,
        };

        var items = CardDeckLayoutMath.Compute(new LayoutSize(700, 800), itemCount: 4, settings);

        // 3 columns fit: row width = 3*200 + 2*20 = 640; gutter = (700-640)/2 = 30
        items[0].Rect.Should().Be(new LayoutRect(30, 0, 200, 100));
        items[1].Rect.Should().Be(new LayoutRect(250, 0, 200, 100));
        items[2].Rect.Should().Be(new LayoutRect(470, 0, 200, 100));
        items[3].Rect.Should().Be(new LayoutRect(30, 120, 200, 100));
    }

    [Test]
    public void Layout_is_finite_for_common_inputs()
    {
        var settings = CardDeckLayoutSettings.Default;
        var items = CardDeckLayoutMath.Compute(new LayoutSize(1024, 768), itemCount: 25, settings);

        items.Should().HaveCount(25);
        items.All(i => i.Rect.IsFinite && i.Transform.IsFinite).Should().BeTrue();
    }
}
