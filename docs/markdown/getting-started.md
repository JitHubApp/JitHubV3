# Markdown (Developer Guide)

This repo includes a standalone Markdown engine + renderer stack:

- **Core** (`JitHub.Markdown.Core`): parsing (Markdig → internal model), layout, selection mapping, hit-testing.
- **Skia** (`JitHub.Markdown.Skia`): SkiaSharp rendering, text shaping (HarfBuzz), syntax highlighting.
- **Uno** (`JitHub.Markdown.Uno`): Uno/WinUI controls that host the Skia renderer.

## Supported entry points

### Parse

Use `MarkdownEngine` to parse source Markdown into the internal document model:

```csharp
using JitHub.Markdown;

var engine = MarkdownEngine.CreateDefault();
var document = engine.Parse(markdownText);
```

### Layout

Use `MarkdownLayoutEngine` to transform a document into a `MarkdownLayout` for a given width/theme:

```csharp
var layoutEngine = new MarkdownLayoutEngine();
var layout = layoutEngine.Layout(document, theme, maxWidth: 600);
```

### Render

Use `SkiaMarkdownRenderer` + `RenderContext` to draw a layout onto an `SKCanvas`:

```csharp
using SkiaSharp;
using JitHub.Markdown;

var renderer = new SkiaMarkdownRenderer();
renderer.Render(layout, new RenderContext
{
    Canvas = canvas,
    Theme = theme,
    Viewport = new RectF(0, 0, width, height),
    Scale = 1,
    Plugins = engine.Plugins,
});
```

### Uno control

If you’re in a Uno/WinUI UI, prefer `MarkdownView` / `SkiaMarkdownView` (details in the embedding doc).

## Where to go next

- Embedding in Uno/WinUI: [docs/markdown/embedding-uno.md](docs/markdown/embedding-uno.md)
- Theming: [docs/markdown/theming.md](docs/markdown/theming.md)
- Plugins/extension points: [docs/markdown/plugins.md](docs/markdown/plugins.md)
- Selection mapping: [docs/markdown/selection-mapping.md](docs/markdown/selection-mapping.md)
- Release/versioning policy: [docs/markdown/versioning-and-compat.md](docs/markdown/versioning-and-compat.md)
