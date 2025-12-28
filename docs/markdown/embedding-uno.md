# Embedding (Uno/WinUI)

The easiest way to host the renderer in UI code is through the Uno control layer.

## Controls

- `MarkdownView` (recommended): a `UserControl` wrapper which selects the correct XAML/code-backed implementation depending on the build mode.
- `SkiaMarkdownView`: the Skia-backed view that owns the renderer, input, selection, and hit-testing.

## Basic usage

In XAML (when the XAML-backed implementation is active):

```xml
<md:MarkdownView
    x:Name="Markdown"
    HorizontalAlignment="Stretch" />
```

In code-behind / view model:

```csharp
Markdown.Markdown = markdownText;
```

(Exact dependency properties and naming are defined on `MarkdownView` / `SkiaMarkdownView`; search for `DependencyProperty.Register` in `JitHub.Markdown.Uno`.)

## Images

Rendering supports an optional `ImageResolver` callback on `RenderContext`. In Uno, `SkiaMarkdownView` typically supplies this when it renders; if you need custom behavior (caching, auth headers), prefer implementing it at the view level.

## Selection + input

Selection, pointer interaction, and keyboard navigation are handled inside `SkiaMarkdownView`.

Logging categories that are useful while debugging input:

- `JitHub.Markdown.Uno.Input`
- `JitHub.Markdown.Uno.Selection`

See also: [docs/markdown-selection-and-input.md](docs/markdown-selection-and-input.md)
