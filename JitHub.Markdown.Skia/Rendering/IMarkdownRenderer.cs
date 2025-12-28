namespace JitHub.Markdown;

public interface IMarkdownRenderer
{
    void Render(MarkdownLayout layout, RenderContext context);
}
