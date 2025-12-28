namespace JitHub.Markdown;

public static class MarkdownContrast
{
    /// <summary>
    /// Returns WCAG 2.x contrast ratio between two opaque colors.
    /// If colors have alpha, they should be composited over a common background first.
    /// </summary>
    public static double ContrastRatio(ColorRgba a, ColorRgba b)
    {
        var l1 = RelativeLuminance(a);
        var l2 = RelativeLuminance(b);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Returns WCAG relative luminance for sRGB colors.
    /// Alpha is ignored.
    /// </summary>
    public static double RelativeLuminance(ColorRgba color)
    {
        static double ToLinear(byte c)
        {
            var srgb = c / 255.0;
            return srgb <= 0.04045
                ? srgb / 12.92
                : Math.Pow((srgb + 0.055) / 1.055, 2.4);
        }

        var r = ToLinear(color.R);
        var g = ToLinear(color.G);
        var b = ToLinear(color.B);

        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }
}
