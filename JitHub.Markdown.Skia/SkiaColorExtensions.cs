using SkiaSharp;

namespace JitHub.Markdown;

internal static class SkiaColorExtensions
{
    public static SKColor ToSKColor(this ColorRgba c)
        => new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
}
