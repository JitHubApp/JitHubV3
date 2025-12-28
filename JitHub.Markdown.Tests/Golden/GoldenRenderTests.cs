using NUnit.Framework;
using SkiaSharp;

namespace JitHub.Markdown.Tests.Golden;

[TestFixture]
public sealed class GoldenRenderTests
{
    private static readonly GoldenImageAssert.Options DefaultTolerance = new(
        PerChannelTolerance: 2,
        MaxMismatchedPixelRatio: 0.0025,
        PngQuality: 100);

    public static IEnumerable<TestCaseData> Cases()
    {
        yield return Case(
            name: "paragraph-and-headings",
            markdown: "# Title\n\nHello **world** from *Skia*.\n");

        yield return Case(
            name: "link",
            markdown: "See [Uno](https://platform.uno) now.\n");

        yield return Case(
            name: "inline-code",
            markdown: "Inline `code` here.\n");

        yield return Case(
            name: "code-block",
            markdown: "```csharp\nvar x = 1;\nConsole.WriteLine(x);\n```\n");

        yield return Case(
            name: "blockquote",
            markdown: "> Quote\n>\n> Second line\n");

        yield return Case(
            name: "list-unordered",
            markdown: "- One\n- Two\n- Three\n");

        yield return Case(
            name: "list-ordered",
            markdown: "1. One\n2. Two\n3. Three\n");

        yield return Case(
            name: "table",
            markdown: "| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |\n");

        yield return Case(
            name: "thematic-break",
            markdown: "Above\n\n---\n\nBelow\n");

        static TestCaseData Case(string name, string markdown)
            => new TestCaseData(new GoldenCase(name, markdown))
                .SetName($"Golden_{name}");
    }

    [TestCaseSource(nameof(Cases))]
    public void Render_matches_golden(GoldenCase @case)
    {
        using var bitmap = Render(@case.Markdown, width: 800, theme: GoldenTheme.Light);
        GoldenImageAssert.MatchesBaseline(bitmap, @case.Name, DefaultTolerance);
    }

    private static SKBitmap Render(string markdown, int width, MarkdownTheme theme)
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse(markdown);

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: width, theme: theme, scale: 1, textMeasurer: measurer);

        var height = Math.Max(1, (int)Math.Ceiling(layout.Height));
        using var surfaceBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(surfaceBitmap);

        var bg = theme.Colors.PageBackground;
        canvas.Clear(new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A));

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, width, height),
            Scale = 1,
            Overscan = 0,
        });

        // Return a detached bitmap (surfaceBitmap is disposed when leaving scope).
        return surfaceBitmap.Copy() ?? throw new InvalidOperationException("Unable to copy rendered bitmap");
    }

    public sealed record GoldenCase(string Name, string Markdown);

    private static class GoldenTheme
    {
        // Deterministic: avoid system font family names (e.g. Consolas) so SkiaTypefaceCache
        // will use the embedded fonts from JitHub.Markdown.Skia.
        public static MarkdownTheme Light { get; } = new()
        {
            Colors = MarkdownTheme.Light.Colors,
            Metrics = MarkdownTheme.Light.Metrics,
            Selection = MarkdownTheme.Light.Selection,
            Typography = new MarkdownTypography
            {
                Paragraph = MarkdownTextStyle.Default(ColorRgba.Black),
                Heading1 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 28f, weight: FontWeight.Bold),
                Heading2 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 24f, weight: FontWeight.Bold),
                Heading3 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 20f, weight: FontWeight.SemiBold),
                Heading4 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 18f, weight: FontWeight.SemiBold),
                Heading5 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 16f, weight: FontWeight.SemiBold),
                Heading6 = MarkdownTextStyle.Default(ColorRgba.Black).With(fontSize: 16f, weight: FontWeight.SemiBold),
                InlineCode = new MarkdownTextStyle(
                    FontFamily: null,
                    FontSize: 14f,
                    Weight: FontWeight.Normal,
                    Italic: false,
                    Underline: false,
                    Foreground: ColorRgba.Black),
                Link = MarkdownTextStyle.Default(ColorRgba.FromRgb(0, 102, 204)).With(underline: true),
            },
        };
    }
}
