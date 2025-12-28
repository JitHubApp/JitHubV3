using FluentAssertions;
using NUnit.Framework;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownThemeEngineTests
{
    [Test]
    public void Resolve_returns_expected_preset()
    {
        MarkdownThemeEngine.Resolve(MarkdownThemeVariant.Light).Colors.PageBackground.Should().Be(MarkdownTheme.Light.Colors.PageBackground);
        MarkdownThemeEngine.Resolve(MarkdownThemeVariant.Dark).Colors.PageBackground.Should().Be(MarkdownTheme.Dark.Colors.PageBackground);
        MarkdownThemeEngine.Resolve(MarkdownThemeVariant.HighContrast).Colors.PageBackground.Should().Be(MarkdownTheme.HighContrast.Colors.PageBackground);
    }

    [Test]
    public void Resolve_applies_partial_color_override_only()
    {
        var theme = MarkdownThemeEngine.Resolve(
            MarkdownThemeVariant.Light,
            overrides: new MarkdownThemeOverrides
            {
                Colors = new MarkdownColorsOverrides
                {
                    ThematicBreak = ColorRgba.FromRgb(1, 2, 3),
                }
            });

        theme.Colors.ThematicBreak.Should().Be(ColorRgba.FromRgb(1, 2, 3));
        theme.Colors.PageBackground.Should().Be(MarkdownTheme.Light.Colors.PageBackground);
        theme.Typography.Paragraph.Should().Be(MarkdownTheme.Light.Typography.Paragraph);
    }

    [Test]
    public void Presets_meet_minimum_contrast_for_paragraph_text()
    {
        AssertThemeTextContrast(MarkdownTheme.Light, minRatio: 4.5);
        AssertThemeTextContrast(MarkdownTheme.Dark, minRatio: 4.5);
        AssertThemeTextContrast(MarkdownTheme.HighContrast, minRatio: 7.0);
    }

    [Test]
    public void Presets_keep_selection_overlay_readable()
    {
        AssertSelectionReadable(MarkdownTheme.Light, minRatio: 4.5);
        AssertSelectionReadable(MarkdownTheme.Dark, minRatio: 4.5);
        AssertSelectionReadable(MarkdownTheme.HighContrast, minRatio: 7.0);
    }

    [Test]
    public void Engine_repairs_unreadable_selection_text_by_choosing_black_or_white()
    {
        // Force an unreadable combination: black selection text over a near-black blended selection fill.
        var theme = MarkdownThemeEngine.Resolve(
            MarkdownThemeVariant.Dark,
            overrides: new MarkdownThemeOverrides
            {
                Colors = new MarkdownColorsOverrides
                {
                    PageBackground = ColorRgba.Black,
                },
                Selection = new MarkdownSelectionOverrides
                {
                    SelectionFill = ColorRgba.FromArgb(200, 0, 0, 0),
                    SelectionText = ColorRgba.Black,
                }
            });

        var blendedFill = theme.Selection.SelectionFill.BlendOver(theme.Colors.PageBackground);
        var ratio = MarkdownContrast.ContrastRatio(theme.Selection.SelectionText, blendedFill);

        ratio.Should().BeGreaterThanOrEqualTo(3.0);
        (theme.Selection.SelectionText.Equals(ColorRgba.Black) || theme.Selection.SelectionText.Equals(ColorRgba.White)).Should().BeTrue();
    }

    private static void AssertThemeTextContrast(MarkdownTheme theme, double minRatio)
    {
        var ratio = MarkdownContrast.ContrastRatio(theme.Typography.Paragraph.Foreground, theme.Colors.PageBackground);
        ratio.Should().BeGreaterThanOrEqualTo(minRatio);
    }

    private static void AssertSelectionReadable(MarkdownTheme theme, double minRatio)
    {
        var blendedFill = theme.Selection.SelectionFill.BlendOver(theme.Colors.PageBackground);
        var ratio = MarkdownContrast.ContrastRatio(theme.Selection.SelectionText, blendedFill);
        ratio.Should().BeGreaterThanOrEqualTo(minRatio);
    }
}
