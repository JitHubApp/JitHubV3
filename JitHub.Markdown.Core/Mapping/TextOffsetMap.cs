using System.Diagnostics;

namespace JitHub.Markdown;

/// <summary>
/// Maps offsets in rendered/display text back to offsets in the original markdown source.
/// Phase 1 scope: supports mapping for inline text/link/emphasis trees.
/// </summary>
[DebuggerDisplay("Len={RenderedText.Length}")]
internal sealed class TextOffsetMap
{
    public TextOffsetMap(string renderedText, int[] renderedToSourceIndex)
    {
        RenderedText = renderedText;
        RenderedToSourceIndex = renderedToSourceIndex;
    }

    public string RenderedText { get; }

    /// <summary>
    /// For each character in <see cref="RenderedText"/>, stores the corresponding source index in the markdown string.
    /// </summary>
    public int[] RenderedToSourceIndex { get; }

    public SourceSpan MapRenderedRangeToSourceSpan(int start, int length)
    {
        if (length <= 0 || RenderedText.Length == 0)
        {
            return new SourceSpan(0, 0);
        }

        start = Math.Clamp(start, 0, RenderedText.Length);
        var end = Math.Clamp(start + length, 0, RenderedText.Length);

        if (end <= start)
        {
            return new SourceSpan(0, 0);
        }

        var min = int.MaxValue;
        var max = int.MinValue;

        for (var i = start; i < end; i++)
        {
            var src = RenderedToSourceIndex[i];
            if (src < 0)
            {
                continue;
            }

            min = Math.Min(min, src);
            max = Math.Max(max, src);
        }

        if (min == int.MaxValue)
        {
            return new SourceSpan(0, 0);
        }

        // max is inclusive; convert to exclusive end.
        return new SourceSpan(min, max + 1);
    }
}
