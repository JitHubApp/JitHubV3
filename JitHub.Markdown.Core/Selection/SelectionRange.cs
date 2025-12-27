namespace JitHub.Markdown;

public readonly record struct SelectionRange(MarkdownHitTestResult Anchor, MarkdownHitTestResult Active)
{
    public MarkdownHitTestResult Start => Compare(Anchor, Active) <= 0 ? Anchor : Active;

    public MarkdownHitTestResult End => Compare(Anchor, Active) <= 0 ? Active : Anchor;

    internal static int Compare(MarkdownHitTestResult a, MarkdownHitTestResult b)
    {
        var c = a.LineIndex.CompareTo(b.LineIndex);
        if (c != 0) return c;

        c = a.RunIndex.CompareTo(b.RunIndex);
        if (c != 0) return c;

        return a.TextOffset.CompareTo(b.TextOffset);
    }
}

public interface ISelectionNormalizer
{
    SelectionRange Normalize(MarkdownLayout layout, SelectionRange range);
}

public sealed class DefaultSelectionNormalizer : ISelectionNormalizer
{
    public static DefaultSelectionNormalizer Instance { get; } = new();

    private DefaultSelectionNormalizer() { }

    public SelectionRange Normalize(MarkdownLayout layout, SelectionRange range)
    {
        _ = layout ?? throw new ArgumentNullException(nameof(layout));
        return SelectionRange.Compare(range.Anchor, range.Active) <= 0 ? range : new SelectionRange(range.Active, range.Anchor);
    }
}
