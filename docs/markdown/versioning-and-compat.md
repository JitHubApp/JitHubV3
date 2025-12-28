# Versioning and compatibility (Markdown packages)

This repo contains multiple Markdown assemblies which are intended to be consumed together.

## SemVer intent

- **Patch**: bug fixes and performance improvements; no breaking changes.
- **Minor**: additive APIs (new node kinds, new theme knobs, new plugin hooks); no breaking changes.
- **Major**: breaking changes (renamed/removed types, changed behavior that affects rendering, selection, layout, or plugin contracts).

## What is considered public and supported

Supported integration points:

- `MarkdownEngine` (+ `MarkdownParserOptions`)
- `MarkdownLayoutEngine`
- Theme types (`MarkdownTheme`, presets, overrides)
- Plugin contract (`IMarkdownRenderPlugin`, `MarkdownPluginRegistry`)
- Selection mapping contract (`SelectionSourceMapper`, `ISelectionSourceIndexMapper`)
- Skia renderer contract (`SkiaMarkdownRenderer`, `RenderContext`, `IMarkdownRenderer`)
- Uno controls (`MarkdownView`, `SkiaMarkdownView`)

Anything else that is public but not listed above should be treated as implementation detail and may change in minor releases.

## Source compatibility

- Prefer calling into the entry points above rather than constructing internal model/layout node types directly.
- If you need deeper hooks, add an explicit plugin contract rather than depending on incidental public types.
