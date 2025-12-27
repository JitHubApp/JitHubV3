namespace JitHub.Markdown;

public readonly record struct TextMeasurement(float Width, float Height)
{
    public SizeF Size => new(Width, Height);
}

public interface ITextMeasurer
{
    TextMeasurement Measure(string text, MarkdownTextStyle style, float scale);

    float GetLineHeight(MarkdownTextStyle style, float scale);
}
