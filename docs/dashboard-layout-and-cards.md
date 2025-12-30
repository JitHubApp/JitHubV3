# Dashboard layout and cards (generic)

This document describes the **generic dashboard card system**: how cards are rendered, laid out, and animated. It is intentionally *provider-agnostic*.

## Core concepts

### `DashboardCardModel`
A dashboard card is a small, self-contained UI unit described by `DashboardCardModel`.

Key points:
- **Identity:** cards are merged into the UI by `CardId` (a `long`). This must be globally unique across *all* cards currently shown.
- **Kind:** `DashboardCardKind` is used for templating, tinting, and UI automation identifiers.
- **Ordering hints:** `Importance` is used to keep ordering stable within a provider.

### Card template
The dashboard uses a shared card template that binds to `DashboardCardModel` and renders:
- Title / subtitle / summary
- Optional actions (primary/secondary)
- A tint variant (semantic color already defined by the app theme)

Rules:
- Do not hardcode colors in XAML.
- Prefer existing theme brushes and shared styles.

## Layout engine

### `CardDeckPresenter` (host)
Cards are presented via a custom host built on top of `ItemsRepeater`.

- Project: `JitHub.Dashboard.Layouts`
- Type: `CardDeckPresenter`

This provides:
- High-density rendering via `ItemsRepeater`
- Cross-platform virtualization-friendly behavior
- A single place to encapsulate layout/animation behavior so providers stay simple

### Responsive behavior
The card host adapts to window size. The goal is a dense, professional layout on wide screens while remaining usable on narrow screens.

Implementation guidance:
- Use structural switches (template/layout changes) via `VisualStateManager` and/or toolkit responsive primitives.
- Use responsive value tuning (margins, spacing) through shared resources (not scattered inline constants).

## Animation and motion

The card system should keep motion subtle and Uno/WinUI-native:
- Favor animating `Opacity` and `CompositeTransform` properties.
- Avoid blocking UI thread during data refresh; cards should appear progressively.

## Testability hooks

The dashboard UI automation uses a stable mapping from `DashboardCardKind` to an automation identifier.

Note: when a provider emits multiple cards of the same kind (feed-style), those cards will share the same kind-based automation id; tests should generally validate counts and presence rather than selecting a specific item unless per-item automation ids are introduced later.
