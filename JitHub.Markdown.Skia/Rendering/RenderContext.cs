using SkiaSharp;

namespace JitHub.Markdown;

public sealed class RenderContext
{
    public required SKCanvas Canvas { get; init; }

    public required RectF Viewport { get; init; }

    public required float Scale { get; init; }

    public float Overscan { get; init; } = 0;
}
