namespace JitHub.Markdown;

using System.Collections.Immutable;

public readonly record struct TextMeasurement(float Width, float Height)
{
    public SizeF Size => new(Width, Height);
}

public interface ITextMeasurer
{
    TextMeasurement Measure(string text, MarkdownTextStyle style, float scale);

    float GetLineHeight(MarkdownTextStyle style, float scale);
}

public readonly record struct TextShapingResult(
    float Width,
    float Height,
    ImmutableArray<float> CaretX,
    bool IsRightToLeft)
{
    public SizeF Size => new(Width, Height);
}

/// <summary>
/// Optional extension to <see cref="ITextMeasurer"/> that provides shaped caret positions for complex scripts.
/// Layout can fall back to simple per-character measurement when a shaper is not available.
/// </summary>
public interface ITextShaper : ITextMeasurer
{
    TextShapingResult Shape(string text, MarkdownTextStyle style, float scale, bool isRightToLeft);
}
