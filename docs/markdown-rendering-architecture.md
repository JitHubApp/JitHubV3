# JitHub Markdown Rendering Library — Architecture (Draft)

## Why this exists
JitHub’s center experience is rendering Markdown, everywhere (Windows App SDK/WinUI, WebAssembly, iOS, Android, macOS, Linux). We need a **single shared rendering engine** to ensure:

- Identical parsing + layout + rendering behavior on every platform
- GitHub Flavored Markdown (GFM) compatibility and extensibility (plugins)
- A **revolutionized selection model** (arbitrary selection ranges, element-aware selection visuals, and lossless mapping back to the Markdown source)
- Accessibility built-in with platform adapters
- High performance with **lazy rendering / virtualization** and aggressive caching

Non-goals (for the first iteration):
- Full GitHub HTML/CSS fidelity
- Arbitrary HTML rendering in Markdown (we will control this via policy)
- Embedding a web browser

---

## High-level decisions

### Parser
- **Markdig** will be the Markdown parser.
- We will enable **GFM-compatible extensions** and allow additional Markdig extensions.
- We will preserve **source spans** (`SourceSpan`) for all block/inline nodes to support selection → source mapping.

### Rendering
- Rendering is **not** XAML-based per element.
- Rendering is performed via **Skia** so the same engine draws everywhere.
- Platform-specific views are thin wrappers that:
  - host the Skia surface
  - supply input events (pointer, keyboard)
  - provide clipboard + accessibility adapters

### Layout & Virtualization
- Markdown is layouted into a **block tree** (paragraphs, headings, lists, code blocks, tables, etc.).
- Only **visible blocks** are measured/drawn (viewport-driven).
- Inline layout uses a “runs” model (text runs, inline code runs, links, emphasis runs, emoji runs, etc.).

---

## Repository structure (proposed)

### New projects
- `JitHub.Markdown.Core` (netstandard2.0 or net8.0+ depending on Uno constraints)
  - Markdig integration
  - Document model (semantic tree)
  - Layout engine (block/inline)
  - Selection model + source mapping
  - Theme/styling system
  - Image/code rendering support (decoding, caching abstractions)
  - Plugin system

- `JitHub.Markdown.Skia` (shared rendering)
  - Skia drawing primitives and rendering pipeline
  - Text shaping abstraction
  - Render cache and invalidation

- `JitHub.Markdown.Uno` (Uno platform adapter)
  - `MarkdownView` control (Skia-backed)
  - Input wiring (pointer/keyboard)
  - Clipboard bridge
  - Accessibility adapter hooks

### Test page in main app
- Add a page in `JitHubV3` (e.g. `Presentation/MarkdownTestPage.xaml` + VM) that hosts `MarkdownView` and loads representative Markdown samples.
- The test page is a **developer-facing harness**, not a user feature (no extra UX beyond showing rendered Markdown and exercising selection/copy).

---

## Implemented so far (Phase 0)

This section reflects what currently exists in the repo (not the full end-state architecture).

### Projects (created + wired)
- `JitHub.Markdown.Core` (net10.0)
  - Contains a minimal `MarkdownEngine` wrapper around a Markdig pipeline (`UseAdvancedExtensions()` baseline).
- `JitHub.Markdown.Skia` (net10.0)
  - Placeholder project referencing Core (Skia rendering comes in later phases).
- `JitHub.Markdown.Uno` (net10.0)
  - Contains a Phase 0 code-only placeholder `MarkdownView` control.
- `JitHub.Markdown.Uno/Xaml/MarkdownView.xaml`
  - XAML placeholder control kept for MSBuild (Full) builds.
- `JitHub.Markdown.Tests` (net10.0)
  - Minimal NUnit tests validating the parsing harness.

### App harness
- `JitHubV3/Presentation/MarkdownTestPage.xaml` hosts `MarkdownView` and binds to a `Markdown` string in its VM.
- The harness is intentionally minimal in Phase 0 (no theming toggles/selection diagnostics yet).

### Dependency management
- `Markdig` is pinned centrally via `Directory.Packages.props`.

### Constraints/decisions captured
- **Uno SDK build constraint:** a WinUI/Uno class library containing XAML may not be buildable with `dotnet build` in this repo configuration (requires `msbuild`). Phase 0 keeps the adapter code-only to unblock iteration.
- **Adapter TFM simplification (temporary):** `JitHub.Markdown.Uno` targets `net10.0` only in Phase 0 to avoid platform package graph conflicts during early scaffolding. Re-evaluate multi-targeting and/or build strategy in Phase 1.

---

## Implemented so far (Phase 1)

### Parsing + model + source mapping
- `MarkdownEngine.Parse()` returns a `MarkdownDocumentModel` containing:
  - `SourceMarkdown`
  - normalized `BlockNode[]` + `InlineNode[]`
  - `SourceMap` (lookup by `NodeId`)
- `MarkdownParserOptions` exists with:
  - Markdig pipeline hook
  - HTML policy flag (currently enforced by dropping HTML blocks in the builder)
- Source spans are preserved as `[Start, EndExclusive)` in `SourceSpan`.
- Stable `NodeId` generation exists (deterministic hashing based on kind/span/ordinal/parent).

### Selection mapping scaffolding
- `TextOffsetMap` + `MarkdownTextMapper` provide a Phase 1 mechanism to map display text offsets (from inline trees) back to source indices.
- Default behavior maps emphasis/link selections to the inner visible content (excluding markup markers).

### Uno adapter (XAML strategy)
- XAML is reintroduced as `JitHub.Markdown.Uno/Xaml/MarkdownView.xaml`, intended for **MSBuild (Full)** builds.
- A code-only `MarkdownView` fallback remains so `dotnet build` continues to work in environments without `MSBuild.exe`.

---

## Implemented so far (Phase 2)

### Theme model + presets
- `JitHub.Markdown.Core/Theming/*` introduces a theme object model:
  - `MarkdownTheme` (typography/colors/metrics/selection + optional `ImageBaseUri`)
  - `MarkdownThemePresets` (Light/Dark/HighContrast)

### Style resolution
- `IMarkdownStyleResolver` defines block + inline style resolution hooks.
- `MarkdownStyleResolver` provides the default mapping from our normalized node kinds to theme styles.

### Tests (gate)
- `JitHub.Markdown.Tests/MarkdownThemingTests.cs` validates preset construction and basic resolution behavior.

---

## Implemented so far (Phase 3)

### Layout primitives + deterministic layout engine
- `JitHub.Markdown.Core/Layout/*` introduces a Phase 3 layout layer:
  - `MarkdownLayout` (block list + `GetVisibleBlockIndices`)
  - block layout records (`ParagraphLayout`, `HeadingLayout`, `CodeBlockLayout`, `BlockQuoteLayout`, `ThematicBreakLayout`)
  - `LineLayout` + `InlineRunLayout` (run bounds + source span)
  - `ITextMeasurer` abstraction for deterministic, platform-independent measurement
- `MarkdownLayoutEngine` can lay out a `MarkdownDocumentModel` deterministically for a given `width`/`theme`/`scale`/`ITextMeasurer`.

### Baseline virtualization
- `MarkdownLayoutEngine.LayoutViewport(...)` produces a layout containing only blocks intersecting a viewport and stops after passing the viewport bottom.
- Baseline per-block caching exists for common non-nested blocks (paragraph/heading/code/thematic break) keyed by `NodeId + width + scale + theme hash`.

### Tests (gate)
- `JitHub.Markdown.Tests/MarkdownLayoutTests.cs` verifies determinism, wrapping, viewport subset behavior, and size invariants.

---

## Implemented so far (Phase 4)

### Skia renderer skeleton
- `JitHub.Markdown.Skia/Rendering/*` contains:
  - `IMarkdownRenderer` + `RenderContext`
  - `SkiaMarkdownRenderer` that clips to a viewport and renders only visible layout blocks.
- `JitHub.Markdown.Skia/Text/*` contains `SkiaTextMeasurer` implementing `ITextMeasurer`.

### Rendering coverage (so far)
- Paragraphs and headings render from `InlineRunLayout` runs (text + basic underline support).
- Block backgrounds are filled using `MarkdownBlockStyle.Background` + corner radius.
- Inline code renders with a theme-driven background surface (padding + corner radius) without affecting code block rendering.
- Code blocks render as a single background surface with padding using the resolved block style.
- Images render as deterministic placeholder surfaces by default; an optional `RenderContext.ImageResolver` can supply a resolved `SKImage`.

### Decorations + links (so far)
- Strikethrough is propagated into layout runs and rendered as a strike line.
- Link runs carry URL metadata; the renderer can collect hit regions for activation/accessibility and supports hover/pressed tinting via `RenderContext`.

### Tests (gate)
- `JitHub.Markdown.Tests/MarkdownSkiaRendererTests.cs` exercises an offscreen render pass (no-throw) for paragraph + heading input.
- Link hit region behavior is unit-tested (URL present and collected).
- Inline code + code block background rendering are unit-tested with pixel checks.
- Image placeholder rendering is unit-tested with a pixel check.

---

## Core architecture

### 1) Pipeline overview

```
Markdown string
  ↓
Markdig parser (GFM + plugins)
  ↓
DocumentBuilder
  ↓
MarkdownDocumentModel (blocks + inlines + source map)
  ↓
LayoutEngine (given width + theme + scale)
  ↓
LayoutTree (virtualizable blocks, inline runs)
  ↓
SkiaRenderer (draw visible layout)
  ↓
Platform View (Uno control)
```

Key invariant: **Every rendered artifact is traceable back to the original Markdown source range**.

---

## Document model

### MarkdownDocumentModel
Represents a parsed document with stable identity and source mapping.

- `MarkdownDocumentModel`
  - `string SourceMarkdown`
  - `BlockNode[] Blocks`
  - `SourceMap SourceMap` (central index)
  - `MarkdownFeatures EnabledFeatures`

### BlockNode / InlineNode
We keep nodes close to Markdig concepts, but normalized for rendering.

- Block nodes: `HeadingBlock`, `ParagraphBlock`, `ListBlock`, `QuoteBlock`, `CodeBlock`, `TableBlock`, `ThematicBreak`, `HtmlBlock` (optional/policy), etc.
- Inline nodes: `TextRun`, `EmphasisRun`, `StrongRun`, `LinkRun`, `ImageRun`, `InlineCodeRun`, `StrikethroughRun`, `TaskListMarker`, etc.

Every node includes:
- `NodeId` (stable id)
- `SourceSpan` (start/end indices into SourceMarkdown)
- `SemanticRole` (for accessibility)

### SourceMap
SourceMap is an index that answers:
- Given a `NodeId`, what source span does it cover?
- Given a visual run (text segment), what exact source span produced it?

Implementation idea:
- Maintain a contiguous array of `SourceSpanEntry { NodeId, Start, End, Kind }`.
- Keep a secondary index for fast lookup by `NodeId`.

---

## Styling & theming (Requirement #1)

We need to customize:
- Typography (family, weight, size, decoration) **per element type**
- Base URL for images
- Global corner radius for elements (code blocks, blockquotes, tables, callouts)
- Background colors for inline code, code blocks, quotes, etc.

### MarkdownTheme
A theme is a structured set of styles; it must be serializable/configurable.

- `MarkdownTheme`
  - `MarkdownTypography Typography`
  - `MarkdownColors Colors`
  - `MarkdownMetrics Metrics` (spacing scale, corner radius, stroke thickness)
  - `Uri? ImageBaseUri`
  - `SelectionTheme Selection`

### Style resolution
Different nodes may require contextual styles (e.g., code inside heading).

- `IMarkdownStyleResolver`
  - `ResolveTextStyle(InlineNode inline, StyleContext ctx)`
  - `ResolveBlockStyle(BlockNode block, StyleContext ctx)`

Default implementation:
- `MarkdownStyleResolver` that merges:
  - global defaults
  - element-type styles
  - inline overrides (emphasis, strong, link)

### Element style granularity
At minimum:
- Headings H1–H6
- Paragraph
- Link
- Inline code
- Code block
- Blockquote
- List (ordered/unordered) + list markers
- Table
- Thematic break
- Image caption/alt text rendering (policy)

---

## Rendering system (Skia)

### Rendering contracts
- `IMarkdownRenderer`
  - `Render(SKCanvas canvas, RenderContext ctx, LayoutSlice slice)`

- `IRenderCache`
  - caches:
    - shaped text
    - measured paragraphs
    - syntax-highlighted code blocks (pre-rendered bitmap or glyph runs)
    - decoded images

### Text shaping
Cross-platform text shaping must be consistent.

We need an abstraction:
- `ITextShaper`
  - `ShapeText(string text, TextStyle style, TextDirection direction)`

Possible implementations:
- Skia shaping (where available)
- HarfBuzz-backed shaping for correctness across platforms

This is a critical risk area; correctness first, optimize later.

### Images
- `IImageLoader`
  - resolves relative image URLs using `MarkdownTheme.ImageBaseUri`
  - supports caching, cancellation, and placeholder draw
  - supports downsampling for performance

### Code blocks
- Start with simple monospaced rendering + background + padding.
- Provide `ICodeHighlighter` plugin extension point for syntax highlighting.

---

## Plugins & extensibility (GFM + “with plugins”)

### Parser extensions
- We accept Markdig extensions via configuration:
  - `MarkdownParserOptions`
    - `Action<MarkdownPipelineBuilder> ConfigurePipeline`

### Render extensions
We mirror Markdig’s renderer architecture conceptually but remain Skia-first:

- `IMarkdownRenderPlugin`
  - `void Register(MarkdownPluginRegistry registry)`

- `MarkdownPluginRegistry`
  - `RegisterBlockRenderer<TBlock>(IBlockRenderer<TBlock>)`
  - `RegisterInlineRenderer<TInline>(IInlineRenderer<TInline>)`
  - `RegisterSpanMapper(ISpanMapper mapper)`
  - `RegisterAccessibilityProvider(IAccessibilityExtension ext)`

This allows:
- custom node types
- new inline visuals (mentions, issue links)
- alternative rendering for existing blocks

---

## Revolutionized selection model (Requirement #2)

### Goals
- Select arbitrary ranges across the entire document like a web page.
- Selection visuals are **element-aware** (e.g., inline code selection differs from normal text) but must feel **continuous**.
- Copy/paste returns the **exact Markdown source substring** with `{startChar, endChar}` in the original Markdown.
- Selection mapping behavior is customizable (snapping rules).

### Selection architecture
Selection is split into:

1) **Hit testing**: map pointer locations to document positions.
2) **Range normalization**: define the canonical selection range.
3) **Visual selection geometry**: rectangles/paths per line/run.
4) **Source mapping**: map selection to markdown indices.

#### DocumentPosition
A stable logical cursor in the layout:
- `DocumentPosition`
  - `BlockId`
  - `InlineRunId`
  - `GlyphIndex` (or UTF-16 offset) within the run

#### SelectionRange
- `SelectionRange`
  - `Start: DocumentPosition`
  - `End: DocumentPosition`
  - `IsReversed`

#### SourceSelection
- `SourceSelection`
  - `StartCharIndex` (inclusive)
  - `EndCharIndex` (exclusive)
  - `string SelectedMarkdown => SourceMarkdown.Substring(Start, End-Start)`

### Source mapping strategy
Markdig nodes provide `SourceSpan` for blocks/inlines, but selection is at glyph granularity.

We require a mapping from **glyphs** back to **source indices**.

Approach:
- During inline layout, create `TextRunLayout` objects that include:
  - `string RenderText` (what is displayed)
  - `RunSourceSpan` (start/end in markdown)
  - `TextOffsetMap` mapping rendered text offsets → source offsets

`TextOffsetMap` is essential because rendered text may differ from source:
- link text vs link destination
- entity decoding
- emphasis markers (`*`, `_`) not displayed
- task list markers

We make this explicit:

- `IRunTextMapper`
  - `BuildMap(MarkdigNode node, string sourceMarkdown) => TextOffsetMap`

Default policy for “lossless copy”:
- Selection copies from the **source** based on the mapped spans.
- For elements where rendered text diverges from source (e.g., links), mapping defaults to the node’s `SourceSpan`.

Customizable policy (“opinionated but configurable”):
- `SelectionMappingMode`
  - `SourceAccurate` (default): return exact markdown substring
  - `ReaderFriendly`: attempt to expand/shrink selection to avoid cutting through syntax markers

Expose:
- `ISelectionNormalizer` to implement snapping (word boundaries, line boundaries, element boundaries)

### Continuous visuals with per-element selection styles
We render selection in two layers:

1) **Base selection layer**: a unified continuous overlay across lines, using a neutral selection brush.
2) **Element decoration layer**: element-specific overlays (e.g., inline code gets rounded highlight, code blocks get a border tint, images get a selection frame).

This guarantees continuity while allowing customization.

- `SelectionTheme`
  - `BaseOverlayBrush`
  - `Dictionary<ElementType, SelectionStyle>`

- `SelectionStyle`
  - `OverlayBrush`
  - `BorderBrush`
  - `CornerRadius`
  - `Opacity`
  - `Padding`

### Copy/paste
- Clipboard integration is platform-specific (Uno adapter).
- Library returns:
  - `SourceSelection` (start/end indices)
  - Selected markdown text
  - Optional “plain text” extracted text (for apps that prefer plain text paste)

---

## Accessibility (Requirement #3)

### Themes
We support:
- Light / Dark
- High contrast
- RTL

Theme integration is split:
- Core library defines tokens and semantics.
- Platform adapters map to native accessibility APIs.

### Semantic tree
We produce an accessibility semantic tree parallel to the layout:

- `AccessibilityNode`
  - `Role` (Heading, Paragraph, Link, Code, Image, List, ListItem, Table, etc.)
  - `Label` (spoken text)
  - `Bounds` (in view coordinates)
  - `Actions` (Activate link, Copy code, etc.)
  - `Children`

### Platform adapters
Because this is a shared library, the core provides:
- `IAccessibilityBridge`
  - `UpdateTree(AccessibilityNode root)`
  - `Announce(string message)`
  - `SetFocus(AccessibilityNodeId id)`

Platform-specific projects provide implementations:
- Windows (WinUI/Uno): map nodes to UIA via Uno/WinUI automation patterns or overlay elements.
- iOS/Android: map nodes to native accessibility nodes.
- WebAssembly: map nodes to ARIA overlay elements when possible.

### Keyboard navigation
Core defines navigation commands:
- Next/Previous focusable element
- Activate (Enter/Space)
- Selection expansion (Shift+arrows)

Platform adapter maps native key events → core commands.

---

## Performance & lazy rendering

### Virtualization model
We virtualize at the **block level**:
- Layout each block independently (given available width and theme).
- Maintain a block layout cache keyed by:
  - block id
  - width
  - theme hash
  - text scale factor

We only:
- measure + layout blocks that intersect the viewport (with a small prefetch margin)
- draw only visible blocks

### Incremental updates
When markdown changes:
- Parse on background thread.
- Diff block trees by `NodeId` (stable hash based on source spans/structure).
- Reuse layout caches for unchanged blocks.

### Resource caching
- Images: memory + disk cache (policy-driven)
- Text shaping: glyph/run caches
- Code blocks: optional cached bitmap for large blocks

### Threading model
- Parsing: background
- Layout: background (safe)
- Rendering: UI thread (Skia canvas)
- Image decode: background + UI invalidate on completion

---

## Security & content policies
Markdown can contain links and images.

Core library provides policies:
- `ILinkPolicy` (allow/deny/transform)
- `IImagePolicy` (allow/deny/transform)
- `IHtmlPolicy` (disable by default)

---

## API sketch (public surface)

### Core
- `MarkdownEngine`
  - `Parse(string markdown, MarkdownParserOptions options) => MarkdownDocumentModel`
  - `CreateLayout(MarkdownDocumentModel doc, LayoutOptions options) => MarkdownLayout`

### View
- `MarkdownView` (Uno adapter)
  - `Markdown` (string)
  - `Theme` (MarkdownTheme)
  - `Plugins` (collection)
  - `SelectionEnabled` (bool)
  - `GetSelection() => SourceSelection`
  - `CopySelectionToClipboardAsync()`

---

## Testing strategy

### Unit tests
- SourceSpan preservation and mapping correctness
- Selection normalization rules
- Theme/style resolution
- Redirect/policy style tests for URLs (image base)

### Golden tests
- Render to bitmap (Skia) and compare against baseline images for a fixed font set.
- Cross-platform snapshot parity tests (same pixels within tolerance).

### Performance tests
- Measure layout time for large documents.
- Scroll FPS and GC pressure.

---

## Rollout plan (incremental)

1) MVP engine: headings/paragraphs/lists/links/inline code/code blocks/blockquote + selection/copy
2) Tables + task lists + strikethrough (GFM)
3) Images (base URL + caching)
4) Accessibility semantic tree + keyboard navigation
5) Plugin API stabilization + syntax highlighting plugin

---

## Open questions / risks

- **Text shaping consistency** across platforms (fonts, ligatures, RTL, emoji): requires careful abstraction.
- **Selection-to-source mapping** for complex constructs (links, emphasis, tables) needs explicit mapping policies.
- Accessibility in a Skia-only view may require **semantic overlays** for best screen reader support.
- WASM + Skia performance: ensure draw batching and avoid excessive allocations.
