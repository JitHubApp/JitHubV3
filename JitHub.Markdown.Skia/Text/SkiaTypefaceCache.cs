using SkiaSharp;

namespace JitHub.Markdown;

internal static class SkiaTypefaceCache
{
    private static readonly Dictionary<CacheKey, SKTypeface> Cache = new();

    private static readonly Lazy<EmbeddedFont?> EmbeddedNotoSansArabic = new(() => LoadEmbeddedFont("Assets.Fonts.NotoSansArabic.ttf"));
    private static readonly Lazy<EmbeddedFont?> EmbeddedNotoSansHebrew = new(() => LoadEmbeddedFont("Assets.Fonts.NotoSansHebrew.ttf"));

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

            SKTypeface? resolved = null;

            if (!string.IsNullOrWhiteSpace(style.FontFamily))
            {
                resolved = SKTypeface.FromFamilyName(style.FontFamily, fontStyle);
            }

            if (resolved is null)
            {
                // Prefer embedded fonts first, then common family fallbacks.
                resolved = EmbeddedNotoSansArabic.Value?.Typeface;
                resolved ??= EmbeddedNotoSansHebrew.Value?.Typeface;

                // Note: family fallbacks are best-effort; availability depends on the browser/OS and app CSS.
                var fallbacks = new[]
                {
                    "Roboto",
                    "Noto Sans Arabic",
                    "Noto Sans Hebrew",
                    "Segoe UI",
                };

                for (var i = 0; i < fallbacks.Length && resolved is null; i++)
                {
                    resolved = SKTypeface.FromFamilyName(fallbacks[i], fontStyle);
                }
            }

            tf = resolved ?? SKTypeface.Default;
            Cache[key] = tf;
            return tf;
        }
    }

    private static EmbeddedFont? LoadEmbeddedFont(string resourceNameSuffix)
    {
        try
        {
            var asm = typeof(SkiaTypefaceCache).Assembly;
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(resourceNameSuffix, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return null;
            }

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null)
            {
                return null;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }

            var data = SKData.CreateCopy(bytes);
            var tf = SKTypeface.FromData(data);
            return tf is null ? null : new EmbeddedFont(data, tf);
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct CacheKey(string? Family, FontWeight Weight, bool Italic);

    private sealed record EmbeddedFont(SKData Data, SKTypeface Typeface);
}
