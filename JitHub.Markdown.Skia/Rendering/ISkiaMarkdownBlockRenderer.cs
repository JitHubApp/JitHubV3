namespace JitHub.Markdown;

/// <summary>
/// Skia renderer extension point for custom block rendering.
/// </summary>
public interface ISkiaMarkdownBlockRenderer
{
    /// <summary>
    /// Render the block. Return true if the block was handled and the built-in renderer should skip it.
    /// </summary>
    bool TryRender(BlockLayout block, RenderContext context, bool isInQuote);
}
