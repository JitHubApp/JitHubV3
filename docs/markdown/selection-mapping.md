# Selection mapping

The renderer computes selection in layout-space (runs/lines). For features like “copy selection as source Markdown” or “map a caret back to source offsets”, we map selection back into source indices.

## Entry point

Use `SelectionSourceMapper.TryMapToSource(...)`:

```csharp
if (SelectionSourceMapper.TryMapToSource(markdown, document, selectionRange, out var sourceSelection))
{
    // sourceSelection.Start / End refer to indices in the original markdown string
}
```

## Custom policies (plugins)

Selection mapping can be customized by registering `ISelectionSourceIndexMapper` implementations via plugins:

```csharp
public sealed class MySelectionMapper : ISelectionSourceIndexMapper
{
    // Implement mapping rules for custom nodes/runs.
}

public sealed class MyPlugin : IMarkdownRenderPlugin
{
    public void Register(MarkdownPluginRegistry registry)
        => registry.RegisterSelectionMapper(new MySelectionMapper());
}
```

The Uno view passes `engine.Plugins.SelectionMappers` into the mapper when mapping UI selection.
