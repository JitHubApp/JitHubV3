using SkiaSharp;

namespace JitHub.Markdown;

internal static class SkiaTypefaceCache
{
    private static readonly Dictionary<CacheKey, SKTypeface> Cache = new();

    public static SKTypeface GetTypeface(MarkdownTextStyle style)
    {
        var key = new CacheKey(style.FontFamily, style.Weight, style.Italic);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var tf))
            {
                return tf;
            }

            var weight = style.Weight switch
            {
                FontWeight.Bold => SKFontStyleWeight.Bold,
                FontWeight.SemiBold => SKFontStyleWeight.SemiBold,
                _ => SKFontStyleWeight.Normal,
            };

            var slant = style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
            var fontStyle = new SKFontStyle(weight, SKFontStyleWidth.Normal, slant);

            tf = SKTypeface.FromFamilyName(style.FontFamily, fontStyle) ?? SKTypeface.Default;
            Cache[key] = tf;
            return tf;
        }
    }

    private readonly record struct CacheKey(string? Family, FontWeight Weight, bool Italic);
}
