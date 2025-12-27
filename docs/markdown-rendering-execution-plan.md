# JitHub Markdown Rendering Library — Execution Plan (Phase-by-Phase)

This plan is derived from [docs/markdown-rendering-architecture.md](docs/markdown-rendering-architecture.md) and is intentionally deep, with sub-phases and testing gates. The goal is to execute like a major product: stable API surfaces, deterministic behavior, cross-platform parity, and “no-regression” test coverage throughout.

---

## Guiding principles (applies to every phase)

1) **Parity-first**
- If a feature is implemented, it must behave identically on all targets where it’s supported.
- Differences must be explicitly documented as “platform gaps” with owners and deadlines.

2) **Source-traceability is a hard invariant**
- Every rendered artifact must be traceable to original markdown source spans.
- Selection and copy must remain correct as features grow.

3) **Performance is a first-class deliverable**
- Every phase that adds rendering capabilities includes perf profiling and regression checks.
- Lazy rendering/virtualization is not optional.

4) **Tests are the gate**
- Each sub-phase ends with unit tests + (where relevant) golden render tests.
- No large refactors without first freezing behavior via tests.

5) **API stability by layers**
- `Core` APIs stabilize first.
- Render/plugin APIs stabilize next.
- Platform view APIs stabilize last.

---

## Definition of Done (global)

A phase is “done” when:
- Unit tests added and green.
- Any golden tests introduced have baselines + stable tolerances.
- Benchmarks (if applicable) are added and pass on CI.
- Documentation updated (developer doc + public API notes).
- Sample/test page updated to demonstrate the feature.

---

## Phase 0 — Project foundation and repo integration

### Phase 0 status (implemented)

Implemented artifacts:
- Projects added to the repo and solution:
  - `JitHub.Markdown.Core` (net10.0): Markdig-based `MarkdownEngine` skeleton.
  - `JitHub.Markdown.Skia` (net10.0): placeholder project referencing Core.
  - `JitHub.Markdown.Uno` (net10.0): Phase 0 placeholder `MarkdownView` (code-only, no XAML).
  - `JitHub.Markdown.Tests` (net10.0): minimal NUnit tests validating the parsing harness.
- Central Package Management:
  - `Markdig` is pinned centrally via `Directory.Packages.props`.
- App dev harness:
  - `JitHubV3/Presentation/MarkdownTestPage.xaml` + VM are wired into navigation and reachable from the main page.
  - Current harness UX is intentionally minimal: edit a markdown string and see it passed through to the placeholder view.

Decisions / constraints discovered during Phase 0:
- **Uno SDK build constraint:** a WinUI/Uno class library that contains XAML may not be buildable with `dotnet build` in this repo configuration (requires `msbuild`). To keep Phase 0 buildable and unblock iteration, `JitHub.Markdown.Uno` is code-only for now.
- **Adapter TFM simplification:** `JitHub.Markdown.Uno` currently targets **`net10.0` only** to avoid introducing platform runtime package graph conflicts during scaffolding (a WASM runtime package conflict was observed when experimenting with multi-targeting).
- Deferred deliverables: `JitHub.Markdown.GoldenTests` and `JitHub.Markdown.Benchmarks` are intentionally postponed until Phase 2/3.

Build/test gate (Phase 0):
- `dotnet build .\\JitHubV3.slnx -c Debug -f net10.0`
- If you hit intermittent `CS2012` file-lock errors on Windows, rerun with `-m:1`.
- `dotnet test .\\JitHubV3.slnx -c Debug -f net10.0 -m:1`

### 0.1 Create project skeletons
Deliverables:
- New projects (initial empty or minimal compilable):
  - `JitHub.Markdown.Core`
  - `JitHub.Markdown.Skia`
  - `JitHub.Markdown.Uno`
  - `JitHub.Markdown.Tests` (unit tests)
  - `JitHub.Markdown.GoldenTests` (optional initially, can be introduced in Phase 2/3)
  - `JitHub.Markdown.Benchmarks` (optional initially, can be introduced in Phase 3)
- Add projects to the solution structure (`JitHubV3.slnx`).

Unit tests:
- “Project compiles” isn’t a test, but we add a minimal `Core` test verifying the test harness works.

### 0.2 Dependency selection + version pinning
Deliverables:
- Add Markdig reference.
- Add SkiaSharp references consistent with Uno targets.
- Decide target frameworks (recommended approach):
  - `JitHub.Markdown.Core`: net8.0 (or netstandard2.0 if required by Uno setup; decide early)
  - `JitHub.Markdown.Skia`: net8.0
  - `JitHub.Markdown.Uno`: uses Uno-compatible TFMs via existing Uno setup.
- Confirm licensing compatibility for dependencies.

Unit tests:
- Validate Markdig pipeline builds with intended extensions.

### 0.3 Add “Markdown Test Page” harness to `JitHubV3`
Deliverables:
- A developer page in app that hosts the control and loads sample markdown strings.
- Basic input toggles through VM (not extra UX):
  - Set markdown text
  - Switch between theme presets (light/dark/high contrast)
  - Show selection start/end indices and selected markdown

Unit tests:
- App-level unit tests are optional; keep this as a dev harness.

---

## Phase 1 — Parsing layer and source mapping foundation

### Phase 1 status (implemented)

Implemented artifacts:
- `JitHub.Markdown.Core`
  - `MarkdownParserOptions` (pipeline hook + HTML policy flag).
  - `MarkdownEngine.Parse()` now returns `MarkdownDocumentModel` (not raw Markdig AST).
  - Normalized node model (blocks + inlines) with deterministic `NodeId` + `SourceSpan`.
  - `SourceMap` for `NodeId` → `SourceSpan` lookup.
  - `DocumentBuilder` converting Markdig AST → our nodes (Phase 1 coverage: headings, paragraphs, blockquotes, lists (+ task marker heuristic), fenced code, tables, links/images, emphasis/strong/strikethrough via delimiter detection).
- Selection mapping scaffolding:
  - `TextOffsetMap` + `MarkdownTextMapper` (maps rendered/display inline text offsets back to source indices).
  - Default behavior maps emphasis/link selections to inner visible content spans.

Build/test gate (Phase 1):
- `dotnet test .\\JitHub.Markdown.Tests\\JitHub.Markdown.Tests.csproj -c Debug -f net10.0`
- `dotnet test .\\JitHubV3.slnx -c Debug -f net10.0 -m:1`

Notes:
- The model intentionally ignores unsupported/unknown Markdig nodes in Phase 1 (we expand coverage in later phases).
- Task list items are detected via a simple source-text heuristic in Phase 1; we can switch to Markdig metadata later.

### 1.1 Markdig pipeline configuration (GFM baseline)
Deliverables:
- `MarkdownParserOptions` with:
  - pipeline configuration hook
  - default GFM extension set
  - HTML policy flag (disabled by default)
- A `MarkdownEngine.Parse()` that returns a `MarkdownDocumentModel`.

Unit tests:
- Parse basic GFM cases and assert node types/spans exist:
  - headings, emphasis, strong, strikethrough
  - lists (ordered/unordered)
  - task list items
  - fenced code blocks
  - blockquotes
  - tables
  - links/images

### 1.2 Establish SourceSpan preservation rules
Deliverables:
- Define a consistent mapping strategy:
  - Every `BlockNode`/`InlineNode` includes a source span.
  - Every “rendered run” references a source span.
- Introduce `SourceMap` and store `SourceSpanEntry[]`.

Unit tests:
- For each element type: assert spans cover the correct substring.
- Regression tests for tricky spans:
  - nested emphasis markers
  - lists with indentation
  - tables with pipes
  - code fences

### 1.3 Document model normalization
Deliverables:
- Implement `DocumentBuilder` converting Markdig AST → our nodes.
- Define stable `NodeId` strategy:
  - derived from `(kind, sourceSpan, structural index)` or similar deterministic hashing.

Unit tests:
- “Stable NodeId” tests for identical inputs.
- “Small edits” tests (edit markdown) ensure most unchanged blocks keep ids.

### 1.4 Text mapping scaffolding (rendered text vs source text)
Deliverables:
- Introduce `TextOffsetMap` concept.
- Establish default behavior:
  - if mapping is ambiguous (e.g., link destination), map selection to node span.

Unit tests:
- Link mapping tests:
  - selection inside link text maps to correct source substring.
- Emphasis mapping tests:
  - selecting emphasized word should either:
    - map to inside the markers, or
    - map to full node span depending on policy.
  (Implement one as default and keep it consistent.)

---

## Phase 2 — Styling system and theme infrastructure

### Phase 2 status (implemented)

Implemented artifacts:
- `JitHub.Markdown.Core/Theming/*`
  - `MarkdownTheme` + `MarkdownTypography`/`MarkdownColors`/`MarkdownMetrics`/`MarkdownSelectionTheme`
  - `MarkdownThemePresets` (Light/Dark/HighContrast)
  - `IMarkdownStyleResolver` + default `MarkdownStyleResolver`
- `JitHub.Markdown.Tests/MarkdownThemingTests.cs`
  - Verifies presets construct and basic resolver behavior for key inline/block kinds.

Notes / deferred within Phase 2:
- Serialization/deserialization is not implemented.
- Contrast threshold checks are not implemented (tests currently validate structural sanity only).
- RTL-specific knobs are not modeled yet (will be introduced with layout/text shaping work).

### 2.1 Theme object model
Deliverables:
- Implement `MarkdownTheme` with:
  - typography per element type
  - colors (inline code background, code block background, quote background, selection base overlay)
  - metrics (spacing scale, corner radius)
  - `ImageBaseUri`

Unit tests:
- Theme serialization/deserialization (if supported).
- Style lookup tests for each element.

### 2.2 Style resolution rules
Deliverables:
- `IMarkdownStyleResolver` + default implementation.
- Define precedence rules:
  - global → element → inline modifier.

Unit tests:
- Ensure emphasis/strong/link modifies base style without losing typography.

### 2.3 Theme presets
Deliverables:
- Provide built-in presets:
  - Light
  - Dark
  - HighContrast
  - RTL-friendly baseline settings

Unit tests:
- Ensure presets satisfy minimum contrast thresholds (approximate checks).

---

## Phase 3 — Layout engine (virtualization-first)

### Phase 3 status (implemented — baseline)

Implemented artifacts:
- `JitHub.Markdown.Core/Layout/*`
  - Layout primitives: `MarkdownLayout`, `BlockLayout`, `LineLayout`, `InlineRunLayout`, and lightweight geometry (`RectF`, `SizeF`).
  - Text measurement abstraction: `ITextMeasurer` + `TextMeasurement`.
  - `MarkdownLayoutEngine`:
    - deterministic block layout for headings/paragraphs/code blocks/quotes/thematic breaks (given `width`, `theme`, `scale`, `ITextMeasurer`)
    - simple word/whitespace tokenization + wrapping for paragraphs/headings
    - viewport-friendly entry point `LayoutViewport(...)` (returns only intersecting blocks and stops after viewport bottom)
    - basic per-block caching for common non-nested blocks (keyed by block id + width + scale + theme hash)
- `JitHub.Markdown.Tests/MarkdownLayoutTests.cs`
  - determinism tests
  - wrapping tests
  - viewport subset tests
  - no-negative-sizes invariant

Notes / deferred within Phase 3:
- Lists/tables are not laid out yet (they currently flow as “unknown” blocks).
- RTL/shaping is not implemented (unit tests use a deterministic, fake measurer).
- Virtualization is a baseline pass (stops after viewport bottom); full offscreen caching + incremental recompute remains for later phases.

### 3.1 Define layout primitives
Deliverables:
- Layout tree types:
  - `MarkdownLayout`
  - `BlockLayout` (position, size, child inlines)
  - `InlineRunLayout` (text runs, inline code runs, link runs)
- Deterministic measurement for a given width/theme.

Unit tests:
- Layout invariants:
  - no negative sizes
  - deterministic output for same input

### 3.2 Inline text layout (simple)
Deliverables:
- Implement text wrapping for paragraphs/headings.
- Introduce `ITextMeasurer/ITextShaper` abstraction.

Unit tests:
- Wrapping behavior tests with known widths.
- RTL baseline tests: text direction influences shaping.

### 3.3 Block virtualization model
Deliverables:
- `Viewport` concept:
  - visible rect
  - prefetch margin
- Layout only blocks intersecting viewport.
- Cache layout per block:
  - key includes width + theme hash + scale.

Unit tests:
- Cache correctness tests (same key returns same object or equivalent).
- Virtualization tests: offscreen blocks not laid out.

### 3.4 Incremental update strategy
Deliverables:
- Parse + diff documents; reuse block layouts for unchanged blocks.

Unit tests:
- “Edit in middle” test: only affected blocks recomputed.

---

## Phase 4 — Skia renderer (core visuals)

### Phase 4 status (implemented so far: 4.1 + 4.2.1 + 4.2.2)

Implemented artifacts:
- `JitHub.Markdown.Skia`
  - Rendering pipeline skeleton:
    - `IMarkdownRenderer`
    - `RenderContext` (Skia canvas + viewport + scale)
    - `SkiaMarkdownRenderer` renders visible blocks only (viewport clip + layout’s visible indices)
  - `SkiaTextMeasurer` implements `ITextMeasurer` using Skia measurement primitives.
- `JitHub.Markdown.Tests/MarkdownSkiaRendererTests.cs`
  - Offscreen render smoke test (no-throw) for headings + paragraphs.

Notes:
- Only paragraphs, headings, code blocks, blockquotes, and thematic breaks render in baseline form.
- The current SkiaSharp API usage emits obsolescence warnings; we can migrate to `SKFont` APIs once we lock the rendering behaviors.

### 4.1 Rendering pipeline skeleton
Deliverables:
- `IMarkdownRenderer` + `RenderContext`.
- Render only visible layout slices.

Unit tests:
- Minimal render smoke test (does not throw) using an offscreen Skia surface.

### 4.2 Element-by-element rendering (sub-phases)
We implement each element type as a discrete sub-phase with tests.

#### 4.2.1 Paragraphs
- Render text runs
- Apply inline modifiers

Tests:
- Golden tests for paragraph wrapping.
- Unit tests for run ordering.

#### 4.2.2 Headings (H1–H6)
- Element-specific typography
- Spacing rules

Tests:
- Golden tests (H1–H6)

#### 4.2.3 Emphasis / Strong / Strikethrough
- Font weight/decoration

Status (implemented):
- Emphasis + strong are applied during layout (style modifications) and render via the same text pipeline.
- Strikethrough is propagated into layout runs and rendered as a strike line.

Tests:
- Golden tests for nested emphasis.

#### 4.2.4 Links
- Link style + hover/pressed (platform dependent)
- Hit test region creation for accessibility and activation

Status (implemented):
- Link runs carry URL metadata, render with link styling, and support simple hover/pressed tinting via `RenderContext`.
- Renderer collects link hit regions (`HitRegion`) for activation/accessibility wiring.

Tests:
- Unit tests: link run bounds exist
- Golden tests: link visuals

#### 4.2.5 Inline code
- Background brush + corner radius + padding

Status (implemented):
- Inline code runs are laid out with theme-driven padding.
- Skia renderer draws an inline-code surface using `MarkdownTheme.Colors.InlineCodeBackground` and inline-code metrics.

Tests:
- Unit test: renders inline-code background surface (pixel check)

#### 4.2.6 Code blocks
- Monospace typography
- Background surface, padding, optional border

Status (implemented):
- Code blocks use monospace typography via `MarkdownTheme.Typography.InlineCode`.
- Block background + padding are applied via `MarkdownStyleResolver` and rendered as a single surface behind the code.

Tests:
- Unit test: renders code block background surface (pixel check)

#### 4.2.7 Blockquotes
- Quote stripe + background

Status (implemented):
- Blockquotes render a background surface and a theme-driven quote stripe.

Tests:
- Unit test: stripe + background surfaces (pixel check)

#### 4.2.8 Lists (unordered/ordered)
- Marker layout + indentation
- Task list markers

Status (implemented):
- Lists lay out items with a marker gutter and indentation.
- Unordered markers, ordered markers, and task list markers are supported.

Tests:
- Unit test: marker text + indentation (layout)
- Unit test: renderer list pass (no-throw)

#### 4.2.9 Thematic breaks
- Horizontal rule with theme stroke

Tests:
- Golden tests

#### 4.2.10 Tables (GFM)
- Grid layout
- Cell padding

Tests:
- Golden tests: table alignment

#### 4.2.11 Images (implemented)
- Parse image nodes with URL + title + alt
- Layout images as full-width placeholder runs (fixed height)
- Render deterministic placeholder surface; optional resolver hook + base-URI resolution

Tests:
- Unit tests: layout sizing and URL/base-URI resolution behavior
- Golden/pixel tests: placeholder surface (deterministic)

---

## Phase 5 — Selection engine (revolutionized) + clipboard

### 5.1 Hit testing + glyph boundaries (implemented)
Deliverables:
- Per-run glyph boundary X positions (deterministic) to support hit testing.
- `MarkdownHitTester` mapping `(x,y)` → run + text offset.

Unit tests:
- Hit testing returns correct run + offset for known layouts.

### 5.2 Range model and normalization (implemented)
Deliverables:
- `SelectionRange` in layout coordinates.
- `ISelectionNormalizer` with default behavior.

Unit tests:
- Normalization orders anchor/active deterministically.

### 5.3 Visual selection geometry (implemented)
Deliverables:
- Generate selection rects/paths per line.
- Render continuous base overlay.

Unit tests:
- Geometry continuity tests (no gaps for typical wrapped text).
- Renderer draws base selection fill.

### 5.4 Element-aware selection overlays (implemented)
Deliverables:
- Selection styles per element type.
- Apply overlay decorations without breaking continuity.

Unit tests:
- Renderer keeps a continuous base overlay and applies element-aware tint overlays.

### 5.5 Source mapping: selection → markdown substring (implemented)
Deliverables:
- Implement `SourceSelection`.
- Implement `TextOffsetMap` usage in selection.
- Define default mapping policy and expose customization.

Unit tests (heavy):
- Core mapping tests for:
  - emphasis maps to inner content
  - inline code maps to inner code
  - fenced code blocks map to code content

### 5.6 Clipboard integration (implemented)
Deliverables:
- `CopySelectionToClipboardAsync()` in `MarkdownView` via platform adapter.
- Provide both:
  - markdown copy
  - optional plain text copy

Unit tests:
- Core returns correct payload. (Platform clipboard tests are integration tests.)

---

## Phase 6 — Uno Platform view + input (all platforms)

### 6.1 `MarkdownView` control skeleton (implemented)
Deliverables:
- Skia surface hosting
- Bindable properties:
  - `Markdown`
  - `Theme`
  - `SelectionEnabled`
- Renders a document and supports scroll.

Unit tests:
- Basic control lifecycle tests (where feasible).

### 6.2 Input handling: pointer
Sub-phases per platform differences.

#### 6.2.1 Shared pointer model (implemented)
- Pointer down/move/up to update selection
- Tap/click to activate link

Tests:
- Unit tests for gesture state machine.

#### 6.2.2 Windows (WinUI/Uno) (implemented)
- Mouse selection with drag
- Shift+click selection extension
- Right-click context possibility (optional later)

Tests:
- Integration tests if possible; otherwise manual harness checks + core tests.

#### 6.2.3 WebAssembly
- Pointer events via Uno/WASM
- Ensure selection feels like web selection (dragging)

Deferred (later phase):
- Performance + reliability pass for WASM input/rendering:
  - Ensure pointer handlers are registered once (no duplicate processing)
  - Coalesce/throttle `PointerMoved` handling to reduce per-move hit-test + selection churn
  - Reduce log volume in hot paths (move/drag)
  - Profile hit-test + selection update hot paths and remove avoidable allocations

Tests:
- Manual parity verification with harness; core tests cover logic.

#### 6.2.4 iOS/Android
- Touch selection handles are complex; plan as staged:
  - MVP: long-press to start selection, drag to extend.
  - Later: selection handles + magnifier (optional).

Tests:
- Core tests + manual UX verification.

### 6.3 Keyboard navigation (implemented)
Deliverables:
- Arrow keys move selection caret (if selection enabled)
- Tab navigates focusable elements (links)
- Enter activates link

Unit tests:
- Command routing tests in core.

### 6.6 Cleanup + refactors + low-hanging perf (planned)
Goal: incorporate lessons learned from Phase 6 implementation (bugs found/fixed, platform quirks, and reliability/perf hot spots) and leave the codebase in a cleaner, more maintainable state before moving on to accessibility.

#### 6.6.1 Input adapter cleanup (Uno)
Deliverables:
- Replace reflection-based modifier detection for keyboard navigation with an explicit key state tracker:
  - Track Shift via `KeyDown`/`KeyUp` (and/or `PreviewKeyDown`/`PreviewKeyUp`) to support Shift+Tab reliably.
  - Avoid per-keystroke reflection and cross-platform API probing in hot paths.
- Consolidate selection synchronization paths:
  - Single helper to keep `SelectionPointerInteraction` + `SelectionKeyboardInteraction` + `Selection` DP consistent.
  - Ensure pointer interactions always clear keyboard link focus in one place.
- Extract coordinate utilities:
  - Centralize “layout/document ↔ control ↔ ScrollViewer viewport/content” conversions.
  - Prevent future regressions where overlays or hit-testing drift under virtualization/scroll.

Tests:
- Add/extend unit tests for keyboard behaviors in core (already present); keep Uno wiring minimal and deterministic.

#### 6.6.2 Focus + scrolling maturity
Deliverables:
- Ensure focused-link UI stays aligned under scroll virtualization:
  - Link focus rectangle follows document coordinates and updates on scroll changes.
  - Focus rectangle should never appear “stuck” relative to the screen.
- Auto-scroll on focus changes:
  - When Tab/Shift+Tab moves focus off-screen, call into the platform `ScrollViewer` to bring it into view.
  - Add a small top/bottom padding so the focused link isn’t flush to the viewport edge.
- Extend auto-scroll to caret navigation:
  - Arrow key caret movement should keep the caret visible (similar to a TextBox).

Tests:
- Manual harness verification on Desktop + WASM:
  - Long documents with many links.
  - Verify auto-scroll correctness when the markdown view is not at content origin (header above it).

#### 6.6.3 Rendering performance low-hanging fruits
Deliverables:
- Reduce per-render allocations:
  - Reuse the render pixel buffer (e.g., `ArrayPool<byte>` or a cached `byte[]` sized to current bitmap).
  - Avoid allocating a new `byte[]` every frame in `RenderToBitmap`.
- Coalesce invalidations:
  - Throttle/merge frequent `InvalidateRender()` calls from `ScrollViewer.ViewChanged`.
  - Prefer one render per UI frame/tick when multiple events occur.
- Logging hygiene:
  - Remove or gate hot-path logs (pointer move/drag) behind a debug flag or log level checks.

Acceptance:
- No regression in selection/link activation correctness.
- Noticeably reduced GC pressure during scroll/drag on WASM.

#### 6.6.4 Hit-testing performance + structure
Deliverables:
- Accelerate nearest-line hit testing:
  - Precompute a flattened “lines index” (line bounds/vertical ranges) during layout.
  - Use binary search by Y to find nearest line instead of scanning all lines.
- Reduce duplicate enumerations:
  - Avoid repeated `EnumerateLinesWithIndex(layout)` + `ToList()` in keyboard logic.
- Add simple fast paths:
  - If pointer moved within same line band, skip nearest-line search.

Tests:
- Add a couple of micro regression tests (core) verifying hit-test correctness is unchanged.

#### 6.6.5 API + structure tidy-up
Deliverables:
- Review public surface area:
  - Confirm what is public vs internal across Core/Uno.
  - Normalize naming (`SelectionEnabled` vs `IsSelectionEnabled`, etc.) where inconsistencies exist.
- Remove “implementation scar tissue”:
  - Remove temporary hacks/no-ops, keep comments only where they capture platform invariants.

Output:
- Short “Phase 6 lessons learned” note in this plan section to document platform quirks we had to account for.

---

## Phase 7 — Accessibility (per platform) + RTL

### 7.1 Core accessibility semantic tree
Deliverables:
- Produce `AccessibilityNode` tree for visible layout.
- Stable node ids.

Unit tests:
- Tree correctness tests for sample markdown:
  - headings
  - lists
  - link presence

### 7.2 Platform accessibility bridges
Sub-phases per platform.

#### 7.2.1 Windows (UIA)
- Map semantic nodes to UI automation structure.
- Focus and activation actions.

Tests:
- Automation smoke tests if possible; otherwise manual screen reader checks.

#### 7.2.2 WebAssembly (ARIA overlay)
- Optional ARIA overlay for screen readers.
- Keep overlays aligned with layout bounds.

Tests:
- Manual screen reader checks + DOM snapshot tests (if feasible).

#### 7.2.3 iOS (UIAccessibility)
- Provide accessibility elements and labels.

Tests:
- Manual VoiceOver verification + core tree tests.

#### 7.2.4 Android (TalkBack)
- Provide accessibility nodes.

Tests:
- Manual TalkBack verification + core tree tests.

### 7.3 RTL support end-to-end
Deliverables:
- Text shaping respects RTL.
- Layout aligns correctly.

Unit tests:
- RTL shaping/layout tests with known samples.

### 7.4 High contrast theme support
Deliverables:
- High contrast preset + style overrides.

Unit tests:
- Contrast checks and ensuring selection overlays remain readable.

---

## Phase 8 — Plugins and advanced rendering

### 8.1 Plugin registry
Deliverables:
- `IMarkdownRenderPlugin` + registry
- Allow plugins to:
  - extend parser pipeline
  - register custom renderers
  - register selection mappers

Unit tests:
- Plugin registration ordering tests.

### 8.2 Syntax highlighting plugin (optional but planned)
Sub-phases:
- 8.2.1 Identify highlighter (TextMate grammar, etc.)
- 8.2.2 Implement `ICodeHighlighter`
- 8.2.3 Cache highlighted output

Tests:
- Golden tests for highlighted blocks.

### 8.3 GitHub-specific enrichments (future)
- mentions, issue links, PR links, commit hashes

Tests:
- Parser + renderer plugin tests.

---

## Phase 9 — Quality: golden tests, benchmarks, CI

### 9.1 Golden render tests
Deliverables:
- Deterministic font selection for tests.
- Render markdown → bitmap on CI.
- Image diff tolerance policy.

Unit tests:
- Golden baselines for each element type.

### 9.2 Benchmarks
Deliverables:
- Benchmark parsing, layout, and render for large docs.
- Track allocations.

Pass criteria:
- Set target thresholds per platform (mobile emphasis).

### 9.3 Continuous integration gates
Deliverables:
- CI pipeline runs:
  - unit tests
  - golden tests (where supported)
  - benchmarks (optional nightly)

---

## Phase 10 — Documentation and API stabilization

### 10.1 Public API review
Deliverables:
- Define what is public vs internal.
- Add docs for extension points.

### 10.2 Developer documentation
Deliverables:
- How to embed `MarkdownView`
- How to theme
- How to create plugins
- How selection mapping policies work

### 10.3 Release strategy
Deliverables:
- Versioning policy
- Compatibility notes

---

## Phase-by-phase acceptance milestones

Milestone A (end of Phase 2):
- Parse GFM, preserve spans, theme model exists, no rendering yet.

Milestone B (end of Phase 4):
- Visible rendering for core elements + virtualization works.

Milestone C (end of Phase 5):
- Selection works across arbitrary ranges and copy returns exact markdown substring + indices.

Milestone D (end of Phase 7):
- Accessibility works on Windows + WASM + mobile with semantic tree + keyboard nav.

Milestone E (end of Phase 9):
- Golden tests + perf benchmarks + CI gates prevent regressions.

---

## Immediate next steps (ready to execute)

1) Scaffold the new projects and add them to `JitHubV3.slnx`.
2) Add the `MarkdownTestPage` in `JitHubV3` to host the future `MarkdownView`.
3) Implement Phase 1.1–1.2 with Markdig pipeline + spans, plus unit tests.
