# Markdown Selection & Input — Postmortems + Invariants

This document captures the *root causes* behind the selection/input bugs we fixed in the Skia-based Markdown surface, and the **hard invariants** we must keep to prevent regressions.

Scope:
- Uno adapter: `JitHub.Markdown.Uno/SkiaMarkdownView.cs`
- Core hit-testing + selection: `JitHub.Markdown.Core/*`

---

## Mental model

The system has three distinct coordinate spaces:

1) **Layout space (DIPs)**
- All layout bounds (`InlineRunLayout.Bounds`, `LineLayout.Y`, etc.) are expressed in DIPs.
- All hit-testing and selection operates in this space.

2) **Viewport space (DIPs)**
- Pointer event positions retrieved relative to the rendered image are typically in viewport-local coordinates.
- When the image is vertically offset to show only the visible region, the Y is relative to the top of the *visible slice*, not to the full layout.

3) **Raster space (pixels)**
- Rendering uses a pixel-sized `WriteableBitmap` at `XamlRoot.RasterizationScale`.
- The Skia canvas is scaled by rasterization scale (`canvas.Scale(scale)`).
- This scaling must not leak into layout/hit-testing.

**Invariant:** Layout + hit-testing remain in DIPs. Rasterization scale affects *rendering only*.

---

## Fix #1 — Drag-selection while scrolling (mouse wheel)

### Symptom
While dragging to extend a selection, using the mouse wheel to scroll caused the selection to stop tracking the cursor and/or extend incorrectly.

### Root cause
ScrollViewer scrolling can change the viewport (and therefore the pointer’s layout-space Y) **without delivering corresponding `PointerMoved` events** during an active drag.

If the selection state machine only updates on `PointerMoved`, it will keep using stale pointer coordinates.

### Fix
In the Uno adapter, on `ScrollViewer.ViewChanged`, recompute hit-test + selection update using the last cached pointer position (viewport-local) and the new `_viewportTop`.

**Invariant:** Any viewport change during an active pointer selection must trigger a selection refresh from the last known pointer location.

---

## Fix #2 — Link click targets too large

### Symptom
Clicking in the empty whitespace after a link at end-of-line could still activate the link.

### Root cause
Nearest hit-testing typically clamps X to the closest run to provide stable caret placement.
That behavior is correct for caret/selection, but it is **wrong for link activation** if we treat any hit-test result as “clicked the link”.

### Fix
The core pointer interaction (`SelectionPointerInteraction`) only arms link activation if the actual pointer X lies inside the link run’s bounds (with tiny slop).

**Invariant:** Link activation requires the pointer X to be within the link bounds, even when hit-testing clamps X.

---

## Fix #3 — “Last 1–2 characters not selectable”

### Symptom
At the end of some lines/paragraphs, selection would visually stop short; the final 1–2 characters could not be selected reliably.

### Root cause
This was a **geometry desynchronization** bug:
- Some layout operations translated run bounds (`InlineRunLayout.Bounds`) without translating the caret boundary arrays (`InlineRunLayout.GlyphX`).
- Hit-testing uses `GlyphX` to map pointer X → caret/offset.
- If `Bounds` move but `GlyphX` does not, hit-testing will “think” the end caret is earlier than the rendered end.

### Fix
Whenever a run is translated horizontally, translate `GlyphX` by the same delta.

**Invariant:** `Bounds` and `GlyphX` are a single coherent geometric object; any translation must move both.

---

## Debugging notes

### High-signal diagnostics
The Uno adapter includes a DEBUG-only diagnostic to detect runs where:
- The pointer is at the visual edge but caret/offset doesn’t reach the run end.

These logs should remain throttled and only in DEBUG builds.

---

## Regression tests worth keeping

- “Translated blocks keep `GlyphX` aligned with `Bounds`” (list/blockquote/table scenarios).
- “Hit-testing can reach end-of-run caret” for long LTR runs.
- “Link activation does not occur from end-of-line whitespace.”
