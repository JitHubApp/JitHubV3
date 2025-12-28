# Plugins and extension points

Plugins are registered when you create a `MarkdownEngine`.

## Registering plugins

Plugins implement `IMarkdownRenderPlugin` and receive a `MarkdownPluginRegistry`:

```csharp
public sealed class MyPlugin : IMarkdownRenderPlugin
{
    public void Register(MarkdownPluginRegistry registry)
    {
        // 1) Extend Markdig pipeline (optional)
        registry.ConfigurePipeline(builder =>
        {
            // builder.Use...()
        });

        // 2) Register renderer-specific helpers (optional)
        // registry.RegisterRenderer(new MySkiaBlockRenderer());

        // 3) Register selection mapping policy (optional)
        // registry.RegisterSelectionMapper(new MySelectionMapper());
    }
}

var engine = MarkdownEngine.Create(options, new MyPlugin());
```

## Renderer registrations

Core doesn’t reference renderer assemblies. Renderers are stored as `object` and retrieved by type:

```csharp
foreach (var r in engine.Plugins.GetRenderers<IMyRendererExtension>())
{
    // use r
}
```

Skia uses this mechanism for `ISkiaMarkdownBlockRenderer`.

## Deterministic ordering

The registry keeps registrations in the order they’re added so results are deterministic.
