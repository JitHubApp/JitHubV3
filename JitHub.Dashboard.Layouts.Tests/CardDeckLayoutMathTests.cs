using System.Linq;
using FluentAssertions;
using JitHub.Dashboard.Layouts;

namespace JitHub.Dashboard.Layouts.Tests;

public sealed class CardDeckLayoutMathTests
{
    private static int ComputeDeckDepth(int itemIndex, int itemCount, int deckMaxVisibleCount)
    {
        var visualIndexFromFront = (itemCount - 1) - itemIndex;
        return Math.Min(Math.Max(0, visualIndexFromFront), deckMaxVisibleCount - 1);
    }

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

        // IMPORTANT: last item is the “front/top” card.
        items[5].Transform.TranslateY.Should().Be(0);

        // The deck has a small, stable per-item Y jitter.
        // Validate the expected “10/20/30” spacing envelope with generous bounds.
        items[4].Transform.TranslateY.Should().BeInRange(8.5, 11.5);
        items[3].Transform.TranslateY.Should().BeInRange(17.0, 23.0);
        items[2].Transform.TranslateY.Should().BeInRange(26.0, 34.0);

        // clamped (further back than max visible) – still around the deepest offset.
        items[1].Transform.TranslateY.Should().BeInRange(26.0, 34.0);
        items[0].Transform.TranslateY.Should().BeInRange(26.0, 34.0);
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
    public void Grid_layout_respects_max_columns_and_never_overlaps()
    {
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Grid,
            CardMinWidth = 200,
            CardHeight = 100,
            Spacing = 16,
            MaxColumns = 2,
        };

        var items = CardDeckLayoutMath.Compute(new LayoutSize(2000, 800), itemCount: 6, settings);
        items.Should().HaveCount(6);

        // With maxColumns=2, the 3rd item must start a new row.
        items[0].Rect.Y.Should().Be(0);
        items[1].Rect.Y.Should().Be(0);
        items[2].Rect.Y.Should().Be(116);

        // Basic non-overlap check.
        for (var i = 0; i < items.Count; i++)
        {
            items[i].Rect.IsFinite.Should().BeTrue();
            items[i].Rect.Width.Should().Be(200);
            items[i].Rect.Height.Should().Be(100);

            for (var j = i + 1; j < items.Count; j++)
            {
                var a = items[i].Rect;
                var b = items[j].Rect;

                var aRight = a.X + a.Width;
                var bRight = b.X + b.Width;
                var aBottom = a.Y + a.Height;
                var bBottom = b.Y + b.Height;

                var overlapX = Math.Min(aRight, bRight) - Math.Max(a.X, b.X);
                var overlapY = Math.Min(aBottom, bBottom) - Math.Max(a.Y, b.Y);

                (overlapX > 0 && overlapY > 0).Should().BeFalse();
            }
        }
    }

    [Test]
    public void Deck_layout_has_stable_depth_envelope_and_z_ordering()
    {
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Deck,
            CardMinWidth = 320,
            CardHeight = 180,
            DeckMaxVisibleCount = 4,
            DeckOffsetY = 10,
            DeckAngleStepDegrees = 2,
            DeckScaleStep = 0.02,
            DeckBaseZ = 8,
            DeckZStep = 6,
        };

        const int itemCount = 8;
        var items = CardDeckLayoutMath.Compute(new LayoutSize(600, 800), itemCount, settings);
        items.Should().HaveCount(itemCount);

        // All deck cards share the same base rect.
        items.Select(i => i.Rect).Distinct().Should().HaveCount(1);

        var visibleDepth = Math.Min(itemCount, settings.DeckMaxVisibleCount);

        for (var i = 0; i < items.Count; i++)
        {
            var depth = ComputeDeckDepth(i, itemCount, settings.DeckMaxVisibleCount);
            var transform = items[i].Transform;

            transform.IsFinite.Should().BeTrue();
            items[i].Z.Should().Be(settings.DeckBaseZ + (visibleDepth - 1 - depth) * settings.DeckZStep);

            // Depth 0 is the “front/top” card.
            if (depth == 0)
            {
                transform.TranslateY.Should().Be(0);
                transform.Scale.Should().Be(1);
                transform.RotationDegrees.Should().Be(0);
            }
            else
            {
                // Expected translate Y is depth * offset with small jitter.
                var expected = depth * settings.DeckOffsetY;
                transform.TranslateY.Should().BeInRange(expected - 4, expected + 4);

                // Rotation should stay within the depth-based envelope.
                Math.Abs(transform.RotationDegrees).Should().BeLessThanOrEqualTo(Math.Abs(settings.DeckAngleStepDegrees) * depth);

                // Scale tapers with depth, but should remain positive.
                transform.Scale.Should().BeGreaterThan(0);
                transform.Scale.Should().BeLessThan(1);
            }
        }

        // The last item is front/top and should have the highest Z.
        items[^1].Z.Should().Be(items.Max(i => i.Z));
    }

    [Test]
    public void Auto_mode_switching_is_stable_for_non_finite_thresholds()
    {
        // Non-finite thresholds sanitize to 0, which means any non-negative width selects Grid.
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Auto,
            ModeWidthThreshold = double.NaN,
            CardMinWidth = 320,
            CardHeight = 180,
        };

        var items = CardDeckLayoutMath.Compute(new LayoutSize(1, 1), itemCount: 2, settings);
        items.Select(i => i.Transform.RotationDegrees).All(r => r == 0).Should().BeTrue();
    }

    [Test]
    public void Layout_is_finite_for_common_inputs()
    {
        var settings = CardDeckLayoutSettings.Default;
        var items = CardDeckLayoutMath.Compute(new LayoutSize(1024, 768), itemCount: 25, settings);

        items.Should().HaveCount(25);
        items.All(i => i.Rect.IsFinite && i.Transform.IsFinite).Should().BeTrue();
    }

    [Test]
    public void Layout_is_finite_for_pathological_inputs()
    {
        var settings = CardDeckLayoutSettings.Default with
        {
            Mode = CardDeckLayoutMode.Auto,
            ModeWidthThreshold = double.PositiveInfinity,
            CardMinWidth = double.NaN,
            CardHeight = -10,
            Spacing = double.NegativeInfinity,
            DeckOffsetY = double.NaN,
            DeckAngleStepDegrees = double.PositiveInfinity,
            DeckScaleStep = double.NegativeInfinity,
            DeckBaseZ = double.NaN,
            DeckZStep = double.PositiveInfinity,
        };

        var items = CardDeckLayoutMath.Compute(new LayoutSize(double.NaN, double.PositiveInfinity), itemCount: 50, settings);
        items.Should().HaveCount(50);
        items.All(i => i.Rect.IsFinite && i.Transform.IsFinite).Should().BeTrue();
    }

    [Test]
    public void Layout_is_stable_for_large_item_counts()
    {
        var settings = CardDeckLayoutSettings.Default;

        var items = CardDeckLayoutMath.Compute(new LayoutSize(1280, 720), itemCount: 2000, settings);
        items.Should().HaveCount(2000);
        items.All(i => i.Rect.IsFinite && i.Transform.IsFinite).Should().BeTrue();
    }
}
