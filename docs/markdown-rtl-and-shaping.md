# Markdown RTL + Shaping — Decisions + Invariants

This document captures why RTL support in the Skia Markdown surface is implemented via layout/shaping (not XAML mirroring), and the rules that keep rendering + hit-testing consistent.

---

## Why not use XAML `FlowDirection=RightToLeft`

The markdown surface is rendered into a Skia-backed bitmap.
Applying XAML `FlowDirection=RightToLeft` to the host control can mirror the entire surface (including images, code blocks, and selection visuals), which is incorrect.

**Decision:** Keep the Uno control’s `FlowDirection` fixed to LeftToRight and handle RTL in the markdown **layout engine + shaping**.

---

## What “RTL support” means here

RTL support requires these layers to agree:

1) **Layout direction policy**
- Decide base direction when there are no strong directional characters.
- Split mixed-direction content into separate runs when needed.

2) **Text shaping (HarfBuzz)**
- Shaping must be direction-aware.
- The shaped output must provide *caret boundary positions* compatible with hit-testing.

3) **Hit-testing / selection**
- Caret movement and pointer mapping must respect visual ordering.
- For RTL runs, the logical offset→visual position mapping is inverted.

---

## Key invariants

### Invariant A — Single source of truth for direction
`InlineRunLayout.IsRightToLeft` is the canonical direction signal for a run.

### Invariant B — Caret boundaries are in visual order
`InlineRunLayout.GlyphX` stores caret boundary X positions in visual X order (monotonic increasing).

RTL is handled by mapping logical offsets appropriately (inversion when interpreting offsets).

### Invariant C — Layout/hit-testing are always in DIPs
Rasterization scale only affects drawing.

### Invariant D — Transforms apply to *all* geometric representations
If we shift/translate a run, we must shift:
- `Bounds`
- `GlyphX`
- any other geometry derived from the run (e.g., link rectangles)

---

## Practical implementation notes

- The Uno adapter exposes an `IsRightToLeft` property that controls the layout engine’s `DefaultIsRtl` fallback.
- Strong RTL/LTR characters take precedence over the default fallback.
- Selection/hit-testing uses `GlyphX` for stable caret placement across shaped scripts.

---

## Testing recommendations

- RTL paragraph alignment (right edge vs padding).
- RTL list marker gutter placement (marker on right; content not shifted by left gutter).
- Mixed-direction token splitting into two runs.
- Hit-testing reaches run ends (both RTL and LTR) without requiring pointer overshoot.
