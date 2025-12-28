using System.Collections.Immutable;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace JitHub.Markdown;

internal sealed class SkiaTextShaper : ITextShaper, ITextMeasurerWithFontMetrics
{
    public TextMeasurement Measure(string text, MarkdownTextStyle style, float scale)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TextMeasurement(0, GetLineHeight(style, scale));
        }

        using var font = CreateFont(style, scale);
        EnsureTypefaceSupportsText(font.Typeface, text);
        using var shaper = new SKShaper(font.Typeface!);

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();

        var shaped = shaper.Shape(buffer, font);

        // Some SKShaper implementations can under-report Width for certain shaped runs.
        // Ensure the returned width is at least the paint-measured width so caret/selection
        // can reach the visually drawn end of the run.
        var width = Math.Max(shaped.Width, font.MeasureText(text));

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

        var ascent = Math.Max(0, -metrics.Ascent);
        var descent = Math.Max(0, metrics.Descent);

        if (ascent + descent <= 0)
        {
            var h = GetLineHeight(style, scale);
            ascent = Math.Max(0, h * 0.8f);
            descent = Math.Max(0, h - ascent);
        }

        return new TextFontMetrics(ascent, descent);
    }

    public TextShapingResult Shape(string text, MarkdownTextStyle style, float scale, bool isRightToLeft)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new TextShapingResult(0, GetLineHeight(style, scale), ImmutableArray<float>.Empty, isRightToLeft);
        }

        using var font = CreateFont(style, scale);
        EnsureTypefaceSupportsText(font.Typeface, text);
        using var shaper = new SKShaper(font.Typeface!);

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.Direction = isRightToLeft ? Direction.RightToLeft : Direction.LeftToRight;
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();

        var shaped = shaper.Shape(buffer, font);

        font.GetFontMetrics(out var metrics);
        var height = (metrics.Descent - metrics.Ascent);
        if (height <= 0)
        {
            height = GetLineHeight(style, scale);
        }

        var width = Math.Max(shaped.Width, font.MeasureText(text));
        var caretVisual = BuildCaretX(text.Length, shaped, width);

        // Note: caret boundaries must be monotonic increasing X (visual order) for hit-testing binary search.
        // RTL is handled by inverting logical offsets at hit-test / keyboard-navigation time.
        return new TextShapingResult(width, height, caretVisual, isRightToLeft);
    }

    public static SKTextBlob? CreateTextBlob(string text, MarkdownTextStyle style, float scale, bool isRightToLeft)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        using var font = CreateFont(style, scale);
        EnsureTypefaceSupportsText(font.Typeface, text);
        using var shaper = new SKShaper(font.Typeface!);

        using var buffer = new HarfBuzzSharp.Buffer();
        buffer.Direction = isRightToLeft ? Direction.RightToLeft : Direction.LeftToRight;
        buffer.AddUtf16(text);
        buffer.GuessSegmentProperties();

        var shaped = shaper.Shape(buffer, font);
        if (shaped.Codepoints is null || shaped.Points is null || shaped.Codepoints.Length == 0)
        {
            return null;
        }

        var glyphs = new ushort[shaped.Codepoints.Length];
        for (var i = 0; i < glyphs.Length; i++)
        {
            // Skia glyph IDs are 16-bit.
            glyphs[i] = (ushort)shaped.Codepoints[i];
        }

        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(glyphs, font, shaped.Points);
        return builder.Build();
    }

    private static ImmutableArray<float> BuildCaretX(int textLength, SKShaper.Result shaped, float width)
    {
        // Build an approximate caret boundary array for UTF-16 offsets using cluster boundaries.
        // This is sufficient for selection/hit-testing and supports complex-script shaping.
        if (textLength <= 0)
        {
            return ImmutableArray<float>.Empty;
        }

        width = Math.Max(0, width);
        var cps = shaped.Codepoints;
        var pts = shaped.Points;
        var cls = shaped.Clusters;

        if (cps is null || pts is null || cls is null || cps.Length == 0 || pts.Length == 0 || cls.Length == 0)
        {
            // Fallback: evenly spread across width.
            var fallback = ImmutableArray.CreateBuilder<float>(textLength + 1);
            for (var i = 0; i <= textLength; i++)
            {
                fallback.Add(width * (i / (float)textLength));
            }
            return fallback.ToImmutable();
        }

        // Sort glyphs by X so we can derive non-decreasing visual segments.
        var order = Enumerable.Range(0, pts.Length).ToArray();
        Array.Sort(order, (a, b) => pts[a].X.CompareTo(pts[b].X));

        var glyphStartX = new float[order.Length];
        var glyphEndX = new float[order.Length];
        var glyphCluster = new int[order.Length];

        for (var oi = 0; oi < order.Length; oi++)
        {
            var gi = order[oi];
            glyphStartX[oi] = pts[gi].X;
            glyphCluster[oi] = (int)cls[gi];
        }

        for (var oi = 0; oi < order.Length; oi++)
        {
            var nextX = (oi == order.Length - 1) ? width : glyphStartX[oi + 1];
            glyphEndX[oi] = Math.Max(glyphStartX[oi], nextX);
        }

        // Compute per-cluster start/end X.
        var clusterStart = new Dictionary<int, float>();
        var clusterEnd = new Dictionary<int, float>();
        for (var oi = 0; oi < order.Length; oi++)
        {
            var c = glyphCluster[oi];
            var sx = glyphStartX[oi];
            var ex = glyphEndX[oi];

            if (!clusterStart.TryGetValue(c, out var curS) || sx < curS)
            {
                clusterStart[c] = sx;
            }

            if (!clusterEnd.TryGetValue(c, out var curE) || ex > curE)
            {
                clusterEnd[c] = ex;
            }
        }

        var starts = clusterStart.Keys.Where(k => k >= 0 && k <= textLength).Distinct().OrderBy(k => k).ToArray();
        if (starts.Length == 0)
        {
            // Fallback: evenly spread across width.
            var fallback = ImmutableArray.CreateBuilder<float>(textLength + 1);
            for (var i = 0; i <= textLength; i++)
            {
                fallback.Add(width * (i / (float)textLength));
            }
            return fallback.ToImmutable();
        }

        // Ensure we have a segment that starts at 0.
        if (starts[0] != 0)
        {
            starts = (new[] { 0 }).Concat(starts).Distinct().OrderBy(k => k).ToArray();
            clusterStart.TryAdd(0, 0);
            clusterEnd.TryAdd(0, clusterEnd.Count > 0 ? clusterEnd.Values.Max() : width);
        }

        var caret = new float[textLength + 1];

        // Fill segments between cluster starts.
        for (var si = 0; si < starts.Length; si++)
        {
            var startIndex = starts[si];
            var endIndex = (si == starts.Length - 1) ? textLength : Math.Clamp(starts[si + 1], 0, textLength);
            if (endIndex < startIndex)
            {
                continue;
            }

            var sx = clusterStart.TryGetValue(startIndex, out var ssx) ? ssx : 0f;
            var ex = clusterEnd.TryGetValue(startIndex, out var eex) ? eex : sx;
            var segLen = Math.Max(1, endIndex - startIndex);

            for (var i = startIndex; i <= endIndex; i++)
            {
                var t = (i - startIndex) / (float)segLen;
                caret[i] = sx + ((ex - sx) * t);
            }
        }

        // Enforce monotonic non-decreasing caret X.
        var last = caret[0];
        for (var i = 1; i < caret.Length; i++)
        {
            if (caret[i] < last)
            {
                caret[i] = last;
            }
            last = caret[i];
        }

        // Ensure the end caret reaches the visual run width.
        // Some cluster/advance patterns (or shaping quirks) can yield a final caret that is
        // slightly short of the reported width, which makes the last characters effectively
        // unselectable and causes selection highlight to stop early.
        var end = caret.Length - 1;
        if (end >= 0)
        {
            var expected = Math.Max(0, width);
            if (caret[end] < expected)
            {
                caret[end] = expected;
            }

            if (end > 0 && caret[end] < caret[end - 1])
            {
                caret[end] = caret[end - 1];
            }
        }

        return caret.ToImmutableArray();
    }

    private static SKFont CreateFont(MarkdownTextStyle style, float scale)
    {
        var typeface = SkiaTypefaceCache.GetTypeface(style);
        return new SKFont(typeface, style.FontSize * scale);
    }

    private static void EnsureTypefaceSupportsText(SKTypeface? typeface, string text)
    {
        // No-op: Typeface fallback is handled by the typeface cache.
        _ = typeface;
        _ = text;
    }
}
