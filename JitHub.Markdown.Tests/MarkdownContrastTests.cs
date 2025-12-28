using FluentAssertions;
using NUnit.Framework;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownContrastTests
{
    [Test]
    public void ContrastRatio_black_on_white_is_21()
    {
        var ratio = MarkdownContrast.ContrastRatio(ColorRgba.Black, ColorRgba.White);
        ratio.Should().BeApproximately(21.0, 0.05);
    }

    [Test]
    public void BlendOver_transparent_foreground_returns_background()
    {
        var fg = new ColorRgba(10, 20, 30, 0);
        var bg = new ColorRgba(200, 210, 220, 255);

        fg.BlendOver(bg).Should().Be(bg);
    }
}
