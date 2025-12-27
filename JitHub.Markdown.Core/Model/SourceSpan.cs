namespace JitHub.Markdown;

/// <summary>
/// A source span into the original markdown string.
/// Coordinates are zero-based and use an exclusive end: [Start, EndExclusive).
/// </summary>
public readonly record struct SourceSpan(int Start, int EndExclusive)
{
    public int Length => EndExclusive - Start;

    public bool IsEmpty => Length <= 0;

    public static SourceSpan FromInclusive(int start, int endInclusive)
        => new(start, endInclusive < start ? start : endInclusive + 1);
}
