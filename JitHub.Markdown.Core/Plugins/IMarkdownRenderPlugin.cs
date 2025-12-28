namespace JitHub.Markdown;

/// <summary>
/// Plugins extend the markdown pipeline (parsing), rendering, and selection/source mapping.
/// </summary>
public interface IMarkdownRenderPlugin
{
    void Register(MarkdownPluginRegistry registry);
}
