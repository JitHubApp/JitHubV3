using JitHub.Markdown;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownTextStyleTests
{
    [Test]
    public void Default_sets_expected_baseline()
    {
        var s = MarkdownTextStyle.Default(ColorRgba.Black);
        s.FontFamily.Should().BeNull();
        s.FontSize.Should().Be(16f);
        s.Weight.Should().Be(FontWeight.Normal);
        s.Italic.Should().BeFalse();
        s.Underline.Should().BeFalse();
        s.Foreground.Should().Be(ColorRgba.Black);
    }

    [Test]
    public void With_overrides_only_specified_fields()
    {
        var s = MarkdownTextStyle.Default(ColorRgba.Black);

        var s2 = s.With(fontFamily: "Consolas", fontSize: 14f, italic: true);

        s2.FontFamily.Should().Be("Consolas");
        s2.FontSize.Should().Be(14f);
        s2.Italic.Should().BeTrue();

        // Unspecified properties preserved.
        s2.Weight.Should().Be(FontWeight.Normal);
        s2.Underline.Should().BeFalse();
        s2.Foreground.Should().Be(ColorRgba.Black);
    }
}
