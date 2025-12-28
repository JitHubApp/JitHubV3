using Markdig;

namespace JitHub.Markdown;

/// <summary>
/// Collects plugin registrations in a deterministic order.
///
/// Core does not reference renderer-specific assemblies (e.g., Skia). Renderers are registered
/// as objects and later queried by type from the rendering layer.
/// </summary>
public sealed class MarkdownPluginRegistry
{
    private readonly List<Action<MarkdownPipelineBuilder>> _pipelineConfigurators = new();
    private readonly List<object> _renderers = new();
    private readonly List<ISelectionSourceIndexMapper> _selectionMappers = new();

    public IReadOnlyList<Action<MarkdownPipelineBuilder>> PipelineConfigurators => _pipelineConfigurators;

    public IReadOnlyList<object> Renderers => _renderers;

    public IReadOnlyList<ISelectionSourceIndexMapper> SelectionMappers => _selectionMappers;

    public void ConfigurePipeline(Action<MarkdownPipelineBuilder> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        _pipelineConfigurators.Add(configure);
    }

    public void RegisterRenderer<T>(T renderer) where T : class
    {
        if (renderer is null) throw new ArgumentNullException(nameof(renderer));
        _renderers.Add(renderer);
    }

    public IEnumerable<T> GetRenderers<T>() where T : class
    {
        for (var i = 0; i < _renderers.Count; i++)
        {
            if (_renderers[i] is T typed)
            {
                yield return typed;
            }
        }
    }

    public void RegisterSelectionMapper(ISelectionSourceIndexMapper mapper)
    {
        if (mapper is null) throw new ArgumentNullException(nameof(mapper));
        _selectionMappers.Add(mapper);
    }
}
