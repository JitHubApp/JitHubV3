using SkiaSharp;

namespace JitHub.Markdown;

internal sealed class SkiaTextMeasurer : ITextMeasurerWithFontMetrics
{
    public TextMeasurement Measure(string text, MarkdownTextStyle style, float scale)
    {
        using var font = CreateFont(style, scale);
        var width = font.MeasureText(text ?? string.Empty);

        font.GetFontMetrics(out var metrics);
        var height = (metrics.Descent - metrics.Ascent);
        if (height <= 0)
        {
            height = GetLineHeight(style, scale);
        }

        return new TextMeasurement(width, height);
    }

    public float GetLineHeight(MarkdownTextStyle style, float scale)
    {
        using var font = CreateFont(style, scale);
        font.GetFontMetrics(out var metrics);
        var height = (metrics.Descent - metrics.Ascent);
        return height <= 0 ? Math.Max(1, style.FontSize * 1.4f * scale) : height;
    }

    public TextFontMetrics GetFontMetrics(MarkdownTextStyle style, float scale)
    {
        using var font = CreateFont(style, scale);
        font.GetFontMetrics(out var metrics);

        // Skia: Ascent is typically negative (distance above baseline).
        var ascent = Math.Max(0, -metrics.Ascent);
        var descent = Math.Max(0, metrics.Descent);

        // Fallback if metrics are degenerate.
        if (ascent + descent <= 0)
        {
            var h = GetLineHeight(style, scale);
            ascent = Math.Max(0, h * 0.8f);
            descent = Math.Max(0, h - ascent);
        }

        return new TextFontMetrics(ascent, descent);
    }

    private static SKFont CreateFont(MarkdownTextStyle style, float scale)
    {
        var typeface = SkiaTypefaceCache.GetTypeface(style);
        return new SKFont(typeface, style.FontSize * scale);
    }
}
