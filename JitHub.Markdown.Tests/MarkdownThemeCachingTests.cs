using FluentAssertions;
using NUnit.Framework;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownThemeCachingTests
{
    private sealed class LocalTextMeasurer : ITextMeasurer
    {
        public TextMeasurement Measure(string text, MarkdownTextStyle style, float scale)
        {
            var size = Math.Max(0, style.FontSize * scale);
            var width = string.IsNullOrEmpty(text) ? 0 : (text.Length * size * 0.55f);
            var height = size * 1.2f;
            return new TextMeasurement(width, height);
        }

        public float GetLineHeight(MarkdownTextStyle style, float scale)
            => Math.Max(0, style.FontSize * scale) * 1.2f;
    }

    [Test]
    public void Layout_cache_does_not_keep_stale_block_backgrounds_when_only_colors_change()
    {
        var markdown = "```\ncode\n```";
        var doc = MarkdownEngine.CreateDefault().Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new LocalTextMeasurer();

        var theme1 = MarkdownThemeEngine.Resolve(
            MarkdownThemeVariant.Light,
            overrides: new MarkdownThemeOverrides
            {
                Colors = new MarkdownColorsOverrides
                {
                    CodeBlockBackground = ColorRgba.FromRgb(250, 250, 250),
                }
            });

        var theme2 = MarkdownThemeEngine.Resolve(
            MarkdownThemeVariant.Light,
            overrides: new MarkdownThemeOverrides
            {
                Colors = new MarkdownColorsOverrides
                {
                    CodeBlockBackground = ColorRgba.FromRgb(10, 20, 30),
                }
            });

        var l1 = layoutEngine.Layout(doc, width: 420, theme: theme1, scale: 1, textMeasurer: measurer);
        var l2 = layoutEngine.Layout(doc, width: 420, theme: theme2, scale: 1, textMeasurer: measurer);

        var cb1 = l1.Blocks.OfType<CodeBlockLayout>().Single();
        var cb2 = l2.Blocks.OfType<CodeBlockLayout>().Single();

        cb1.Style.Background.Should().Be(theme1.Colors.CodeBlockBackground);
        cb2.Style.Background.Should().Be(theme2.Colors.CodeBlockBackground);
        cb1.Style.Background.Should().NotBe(cb2.Style.Background);
    }
}
