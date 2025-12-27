namespace JitHub.Markdown;

public readonly record struct SizeF(float Width, float Height)
{
    public static readonly SizeF Empty = new(0, 0);
}

public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool IntersectsWith(RectF other)
        => other.Right > X && other.X < Right && other.Bottom > Y && other.Y < Bottom;
}
