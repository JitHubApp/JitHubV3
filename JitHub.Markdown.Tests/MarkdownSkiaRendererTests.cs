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

    [Test]
    public void Link_runs_have_bounds_and_renderer_collects_hit_regions()
    {
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("See [Uno](https://platform.uno) now.");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: MarkdownTheme.Light, scale: 1, textMeasurer: measurer);

        var linkRuns = layout.Blocks
            .OfType<ParagraphLayout>()
            .SelectMany(p => p.Lines)
            .SelectMany(l => l.Runs)
            .Where(r => r.Kind == NodeKind.Link && r.Url is not null)
            .ToArray();

        linkRuns.Should().NotBeEmpty();
        linkRuns.Any(r => r.Bounds.Width > 0).Should().BeTrue();

        using var bitmap = new SKBitmap(800, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var hitRegions = new List<HitRegion>();
        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Viewport = new RectF(0, 0, 800, 200),
            Scale = 1,
            HitRegions = hitRegions,
        });

        hitRegions.Any(r => r.Kind == NodeKind.Link && r.Url == "https://platform.uno").Should().BeTrue();
    }
}
