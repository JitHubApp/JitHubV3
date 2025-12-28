using System.Reflection;
using BenchmarkDotNet.Attributes;
using SkiaSharp;

namespace JitHub.Markdown.Benchmarks;

[MemoryDiagnoser]
public class BenchMarkdownPipeline
{
    private string _markdown = "";
    private MarkdownEngine _engine = null!;
    private MarkdownLayoutEngine _layoutEngine = null!;
    private SkiaTextMeasurer _measurer = null!;

    private MarkdownDocumentModel _doc = null!;
    private MarkdownLayout _layout = null!;

    private MarkdownTheme _theme = null!;

    [Params("small", "large")]
    public string Sample { get; set; } = "small";

    [Params(600, 900)]
    public int Width { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _engine = MarkdownEngine.CreateDefault();
        _layoutEngine = new MarkdownLayoutEngine();
        _measurer = new SkiaTextMeasurer();
        _theme = DeterministicTheme.Light;

        _markdown = LoadSample(Sample);
        _doc = _engine.Parse(_markdown);
        _layout = _layoutEngine.Layout(_doc, width: Width, theme: _theme, scale: 1, textMeasurer: _measurer);
    }

    [Benchmark(Description = "Parse")]
    public MarkdownDocumentModel Parse()
        => _engine.Parse(_markdown);

    [Benchmark(Description = "Layout")]
    public MarkdownLayout Layout()
        => _layoutEngine.Layout(_doc, width: Width, theme: _theme, scale: 1, textMeasurer: _measurer);

    [Benchmark(Description = "Render")]
    public SKBitmap Render()
    {
        var layout = _layoutEngine.Layout(_doc, width: Width, theme: _theme, scale: 1, textMeasurer: _measurer);
        var height = Math.Max(1, (int)Math.Ceiling(layout.Height));

        var bmp = new SKBitmap(Width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);

        var bg = _theme.Colors.PageBackground;
        canvas.Clear(new SKColor((byte)bg.R, (byte)bg.G, (byte)bg.B, (byte)bg.A));

        var renderer = new SkiaMarkdownRenderer();
        renderer.Render(layout, new RenderContext
        {
            Canvas = canvas,
            Theme = _theme,
            Viewport = new RectF(0, 0, Width, height),
            Scale = 1,
            Overscan = 0,
        });

        return bmp;
    }

    private static string LoadSample(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceSuffix = $"Samples.{name}.md";
        var resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded sample not found: {resourceSuffix}");
        }

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Unable to open embedded sample: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();

        if (name.Equals("large", StringComparison.OrdinalIgnoreCase))
        {
            // Expand deterministically to simulate a large document.
            return text + string.Concat(Enumerable.Repeat(text, 10));
        }

        return text;
    }

    private static class DeterministicTheme
    {
        // Deterministic: avoid system font family names so SkiaTypefaceCache uses embedded fonts.
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
