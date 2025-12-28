namespace JitHub.Markdown;

public static class ColorRgbaExtensions
{
    public static ColorRgba WithAlpha(this ColorRgba color, byte a)
        => new(color.R, color.G, color.B, a);

    /// <summary>
    /// Alpha-composites <paramref name="foreground"/> over <paramref name="background"/>.
    /// </summary>
    public static ColorRgba BlendOver(this ColorRgba foreground, ColorRgba background)
    {
        var fa = foreground.A / 255.0;
        var ba = background.A / 255.0;

        var outA = fa + (ba * (1.0 - fa));
        if (outA <= 0.0)
        {
            return ColorRgba.Transparent;
        }

        var r = ((foreground.R * fa) + (background.R * ba * (1.0 - fa))) / outA;
        var g = ((foreground.G * fa) + (background.G * ba * (1.0 - fa))) / outA;
        var b = ((foreground.B * fa) + (background.B * ba * (1.0 - fa))) / outA;

        static byte ClampToByte(double v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)Math.Round(v);
        }

        return new ColorRgba(
            R: ClampToByte(r),
            G: ClampToByte(g),
            B: ClampToByte(b),
            A: ClampToByte(outA * 255.0));
    }
}
