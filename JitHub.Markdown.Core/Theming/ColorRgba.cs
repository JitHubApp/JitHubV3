using System.Diagnostics;

namespace JitHub.Markdown;

[DebuggerDisplay("#{A:X2}{R:X2}{G:X2}{B:X2}")]
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    public static ColorRgba FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);

    public static ColorRgba FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);

    public static readonly ColorRgba Transparent = new(0, 0, 0, 0);
    public static readonly ColorRgba Black = new(0, 0, 0, 255);
    public static readonly ColorRgba White = new(255, 255, 255, 255);
}
