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
            Theme = MarkdownTheme.Light,
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
            Theme = MarkdownTheme.Light,
            Viewport = new RectF(0, 0, 800, 200),
            Scale = 1,
            HitRegions = hitRegions,
        });

        hitRegions.Any(r => r.Kind == NodeKind.Link && r.Url == "https://platform.uno").Should().BeTrue();
    }

    [Test]
    public void Inline_code_renders_background_surface()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Inline `code` here.");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        var inlineCodeRun = layout.Blocks
            .OfType<ParagraphLayout>()
            .SelectMany(p => p.Lines)
            .SelectMany(l => l.Runs)
            .First(r => r.Kind == NodeKind.InlineCode && !r.IsCodeBlockLine);

        using var bitmap = new SKBitmap(800, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 200),
            Scale = 1,
        });

        var bg = theme.Colors.InlineCodeBackground;
        var expected = new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A);
        var pad = theme.Metrics.InlineCodePadding;
        var x = Math.Clamp((int)MathF.Floor(inlineCodeRun.Bounds.X + MathF.Max(1, pad + 1)), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)MathF.Floor(inlineCodeRun.Bounds.Y + MathF.Max(1, pad + 1)), 0, bitmap.Height - 1);

        bitmap.GetPixel(x, y).Should().Be(expected);
    }

    [Test]
    public void Selection_overlay_renders_base_fill()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("Hello world");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        var line = layout.Blocks.OfType<ParagraphLayout>().Single().Lines.Single();
        var spaceIndex = line.Runs.ToList().FindIndex(r => r.Text == " ");
        spaceIndex.Should().BeGreaterThanOrEqualTo(0);

        var spaceRun = line.Runs[spaceIndex];
        spaceRun.Text.Length.Should().Be(1);

        var anchor = new MarkdownHitTestResult(
            LineIndex: 0,
            RunIndex: spaceIndex,
            Run: spaceRun,
            Line: line,
            TextOffset: 0,
            CaretX: MarkdownHitTester.GetCaretX(spaceRun, 0));

        var active = new MarkdownHitTestResult(
            LineIndex: 0,
            RunIndex: spaceIndex,
            Run: spaceRun,
            Line: line,
            TextOffset: 1,
            CaretX: MarkdownHitTester.GetCaretX(spaceRun, 1));

        var selection = new SelectionRange(anchor, active);
        var geometry = SelectionGeometryBuilder.Build(layout, selection);
        geometry.Rects.Should().NotBeEmpty();

        using var bitmap = new SKBitmap(800, 200);
        using var canvas = new SKCanvas(bitmap);
        var bg = theme.Colors.PageBackground;
        canvas.Clear(new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A));

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 200),
            Scale = 1,
            Selection = selection,
        });

        var r = geometry.Rects[0];
        var sampleX = Math.Clamp((int)MathF.Floor(r.X + (r.Width / 2f)), 0, bitmap.Width - 1);
        var sampleY = Math.Clamp((int)MathF.Floor(r.Y + (r.Height / 2f)), 0, bitmap.Height - 1);

        var actual = bitmap.GetPixel(sampleX, sampleY);

        // Expected blend: selection fill over page background.
        var src = theme.Selection.SelectionFill;
        var dst = theme.Colors.PageBackground;
        var a = src.A / 255f;

        static byte Blend(byte s, byte d, float a)
            => (byte)Math.Clamp((s * a) + (d * (1f - a)), 0, 255);

        var expected = new SKColor(
            Blend(src.R, dst.R, a),
            Blend(src.G, dst.G, a),
            Blend(src.B, dst.B, a),
            255);

        actual.Red.Should().BeInRange((byte)Math.Max(0, expected.Red - 1), (byte)Math.Min(255, expected.Red + 1));
        actual.Green.Should().BeInRange((byte)Math.Max(0, expected.Green - 1), (byte)Math.Min(255, expected.Green + 1));
        actual.Blue.Should().BeInRange((byte)Math.Max(0, expected.Blue - 1), (byte)Math.Min(255, expected.Blue + 1));
    }

    [Test]
    public void Code_block_renders_background_surface()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("```csharp\nvar x = 1;\n```");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        var codeBlock = layout.Blocks.OfType<CodeBlockLayout>().First();

        using var bitmap = new SKBitmap(800, 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 240),
            Scale = 1,
        });

        var bg = theme.Colors.CodeBlockBackground;
        var expected = new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A);
        var inset = MathF.Max(2, theme.Metrics.BlockPadding / 2);
        var x = Math.Clamp((int)MathF.Floor(codeBlock.Bounds.X + inset), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)MathF.Floor(codeBlock.Bounds.Y + inset), 0, bitmap.Height - 1);

        bitmap.GetPixel(x, y).Should().Be(expected);
    }

    [Test]
    public void Blockquote_renders_stripe_and_background_surface()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("> Quote\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        var quote = layout.Blocks.OfType<BlockQuoteLayout>().First();

        using var bitmap = new SKBitmap(800, 240);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 240),
            Scale = 1,
        });

        var pad = theme.Metrics.BlockPadding;
        var stripeX = (int)MathF.Floor(quote.Bounds.X + MathF.Round(pad * 0.35f) + 1);
        var stripeY = (int)MathF.Floor(quote.Bounds.Y + MathF.Round(pad * 0.25f) + 1);
        stripeX = Math.Clamp(stripeX, 0, bitmap.Width - 1);
        stripeY = Math.Clamp(stripeY, 0, bitmap.Height - 1);

        var stripeColor = theme.Colors.ThematicBreak;
        var expectedStripe = new SKColor((byte)stripeColor.R, (byte)stripeColor.G, (byte)stripeColor.B, (byte)stripeColor.A);
        bitmap.GetPixel(stripeX, stripeY).Should().Be(expectedStripe);

        var bgX = (int)MathF.Floor(quote.Bounds.X + pad + 2);
        var bgY = stripeY;
        bgX = Math.Clamp(bgX, 0, bitmap.Width - 1);
        bgY = Math.Clamp(bgY, 0, bitmap.Height - 1);

        var bgColor = theme.Colors.QuoteBackground;
        var expectedBg = new SKColor((byte)bgColor.R, (byte)bgColor.G, (byte)bgColor.B, (byte)bgColor.A);
        bitmap.GetPixel(bgX, bgY).Should().Be(expectedBg);
    }

    [Test]
    public void Renderer_renders_lists_without_throwing()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("- one\n- two\n- [ ] todo\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        layout.Blocks.OfType<ListLayout>().Should().NotBeEmpty();

        using var bitmap = new SKBitmap(800, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 300),
            Scale = 1,
        });

        bitmap.Width.Should().Be(800);
    }

    [Test]
    public void Thematic_break_renders_line()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("---\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        var hr = layout.Blocks.OfType<ThematicBreakLayout>().First();

        using var bitmap = new SKBitmap(800, 200);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 200),
            Scale = 1,
        });

        var lineY = Math.Clamp((int)MathF.Floor(hr.Bounds.Y + (hr.Bounds.Height / 2f)), 0, bitmap.Height - 1);
        var x = Math.Clamp(10, 0, bitmap.Width - 1);

        var c = theme.Colors.ThematicBreak;
        var expected = new SKColor((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        bitmap.GetPixel(x, lineY).Should().Be(expected);
    }

    [Test]
    public void Renderer_renders_tables_without_throwing()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("| A | B |\n|---|---|\n| one | two |\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        layout.Blocks.OfType<TableLayout>().Should().NotBeEmpty();

        using var bitmap = new SKBitmap(800, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 300),
            Scale = 1,
        });

        bitmap.Width.Should().Be(800);
    }

    [Test]
    public void Image_renders_placeholder_surface()
    {
        var theme = MarkdownTheme.Light;
        var engine = MarkdownEngine.CreateDefault();
        var doc = engine.Parse("![Alt](img.png)\n");

        var layoutEngine = new MarkdownLayoutEngine();
        var measurer = new SkiaTextMeasurer();
        var layout = layoutEngine.Layout(doc, width: 600, theme: theme, scale: 1, textMeasurer: measurer);

        var imageRun = layout.Blocks
            .OfType<ParagraphLayout>()
            .SelectMany(p => p.Lines)
            .SelectMany(l => l.Runs)
            .First(r => r.Kind == NodeKind.Image);

        using var bitmap = new SKBitmap(800, 300);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = theme,
            Viewport = new RectF(0, 0, 800, 300),
            Scale = 1,
        });

        var bg = theme.Colors.CodeBlockBackground;
        var expected = new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A);

        var x = Math.Clamp((int)MathF.Floor(imageRun.Bounds.X + 6), 0, bitmap.Width - 1);
        var y = Math.Clamp((int)MathF.Floor(imageRun.Bounds.Y + 6), 0, bitmap.Height - 1);
        bitmap.GetPixel(x, y).Should().Be(expected);
    }
}
