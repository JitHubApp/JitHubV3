using JitHub.Markdown;
using SkiaSharp;

namespace JitHub.Markdown.Tests;

[TestFixture]
public sealed class MarkdownSkiaRendererTests
{
    [Test]
    public void Renderer_renders_paragraphs_and_headings_without_throwing()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("# Title\n\nHello **world** from *Skia*.\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        using var bitmap = new SKBitmap(800, 600);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Viewport = new RectF(0, 0, 800, 600),
            Scale = 1,
            Overscan = 0,
        });

        // If we got here, it's a pass (smoke test).
        bitmap.Width.Should().Be(800);
    }
}
