# JitHubV3 Dashboard UI — Architecture + Execution Plan (Draft)

This document proposes the **official dashboard UI** that users see immediately after login.

It is intentionally written to match the tone and rigor of the existing service-layer and markdown architecture docs, while keeping scope strictly aligned to the UX described in the request.

---

## Ground rules (non-negotiable)

1) **Professional dev-tool look**
- High information density, crisp alignment, consistent surfaces, predictable navigation.
- No “toy UI”: avoid oversized controls, inconsistent spacing, or decorative-only elements.

2) **Completely responsive**
- Must work across Windows + WebAssembly + iOS + Android + macOS + Linux.
- Some features may be desktop-only, but responsiveness is still required for all visible UI.

3) **Fluid and beautiful, Uno-native animations**
- Use WinUI/Uno-supported animation primitives first (Storyboards, VisualStateManager transitions, Composition where supported).
- Prefer implicit/transform/opacity animations that are reliable cross-platform.
- If a required behavior cannot be done with existing Uno abstractions, build a small internal abstraction (not a “framework”).

---

## Scope (what we are building)

### Dashboard page (first page after login)

A three-layer experience:

1) **Left sidebar:** repository list
- A persistent left pane on desktop/tablet.
- Collapses behind a hamburger / overlay on narrow screens.

2) **Center content:** animated card UI
- A card-based feed area that **morphs** between presentations based on available width:
  - On **large enough** screens: a professional, dense **grid** of cards.
  - On **smaller** screens (or narrow windows): a **stacked/deck** of cards.
- The transition between grid and deck must be **smoothly animated on resize** (no hard cuts).
- Cards are fed by pluggable “providers” (activity, public feed, assigned issues/PRs, etc.).
  - Providers are not fixed in this phase; the host UX must be stable independent of provider.

3) **Bottom compose box:** natural-language input
- Centered horizontally.
- Floating above the true edge (not touching the bottom).
- Styled to resemble a modern “ChatGPT-like” chat composer (input surface + subtle affordances), but initially it can be non-functional.

4) **Status bar:** global app status
- Desktop: at the true bottom (already exists in Shell as a global status bar).
- Mobile/narrow: moves to the top and becomes more compact.

Non-goals (explicitly out of scope in this phase):
- A full “GitHub.com parity” dashboard with every widget type.
- Drag-and-drop card reordering and freeform rearranging (prototype-only, not included).
- A manual light/dark theme toggle UI.

---

## Current repo baseline (what we build on)

- Navigation + DI are already Uno.Extensions-based.
- Login navigates to `MainViewModel` after success.
- A global status bar exists in `Shell.xaml` and is driven by `StatusBarViewModel`.
- Repository list + issues list POC exists and uses the service layer and cache invalidation event bus.

The dashboard replaces the current repo-list-centric `MainPage` experience with a split layout that keeps repo navigation always available on large screens.

---

## UX structure (layout architecture)

### Root layout layers

The dashboard is a **single page** (viewmodel-driven) composed of these conceptual layers:

1) **App chrome**
- Top area (only for narrow/mobile): compact status + optional minimal title.
- Left navigation pane: repo list.

2) **Primary workspace**
- Center: Card host (responsive grid/deck).

3) **Overlays**
- Floating compose box overlay anchored to bottom-center.
- Optional transient toasts (later) are not included unless already present.

### Responsive behavior strategy

Use Uno Toolkit’s responsive primitives with a clear separation of responsibilities:

- **Structural changes:** `VisualStateManager` and/or `ResponsiveView` (switch templates for “Narrow” vs “Wide”).
- **Value tuning (spacing/margins/sizes/visibility):** Uno Toolkit `Responsive` markup extension.

This aligns with the guidance from:
- Uno Toolkit Responsive Extension how-to (breakpoints and usage)
- Uno Toolkit `ResponsiveView` how-to (template switching)
- Uno.Extensions responsive shell guidance (NavigationView/TabBar patterns)

### Breakpoints

We will use Uno Toolkit logical breakpoints (Narrowest/Narrow/Normal/Wide/Widest) for property tuning. For template switches, we will define explicit thresholds per control using `ResponsiveLayout`.

---

## Navigation model

### Sidebar behavior

Desktop/tablet:
- Always visible left pane listing repositories.
- Selecting a repo updates the center card providers’ context (repo-aware providers) and/or navigates to repo pages (issues, etc.) depending on the card action.

Mobile/narrow:
- Sidebar is hidden by default.
- A top-left hamburger opens an overlay pane with the repo list.

### Implementation choice

Use `NavigationView` for the left sidebar.

Architect the page so that:
- repo selection is a viewmodel event (`SelectRepoCommand`)
- the list view is just a presenter

---

## Data and extensibility model (card providers)

The dashboard center is powered by “card providers”. Providers are intentionally pluggable so we can iterate on card types without reworking the host layout.

### Key abstractions

Define in the app layer (or a small UI abstraction project):

- `IDashboardCardProvider`
  - Responsible for producing a set of cards given current context (user, selected repo, etc.)
  - Must support cancellation and incremental updates (cache-first, then refresh)

- `DashboardContext`
  - `UserScope` (account)
  - `SelectedRepo` (optional)
  - `Clock`/`Now` (testability)

- `DashboardCardModel`
  - Identity: `string CardId` (stable) + `DashboardCardKind`
  - Content: title/subtitle/summary, optional icon, primary/secondary actions
  - Rendering hint: `Density`, `Importance`, optional `PreferredSpan` (for grid)

Provider examples (not implemented here; these are slots):
- Activity feed provider
- Assigned issues provider
- Assigned PRs provider
- Public feed provider

### Update flow

The host viewmodel is responsible for:
- composing providers
- collecting cards into a single list
- ordering cards by provider priority and card importance
- applying in-place sync to avoid UI flicker

Providers should reuse the existing caching + invalidation architecture:
- cache-first calls
- background refresh
- optional polling for “conversation-like” cards

---

## Center layout: card host + layout engine

This is the core “signature” of the dashboard.

### Requirements

- Smoothly animated card UI that responds to window resize.
- Supports two conceptual presentations and **animates between them**:
  - **Grid / multi-column cards** when the window is “large enough”
  - **Stack / deck** when the window is narrower
- No user drag-reorder.
- Optional programmatic swipe left/right (Tinder-like) for actions (e.g., star a repo).

### Recommended control strategy

- Use `ItemsRepeater` for performance and layout control.
- Use a custom `VirtualizingLayout` for the deck/grid behavior.

Why `ItemsRepeater` + `VirtualizingLayout`:
- Works well for high-density UIs.
- Gives us layout math control without retemplating `ListView`.
- Keeps the animation surface controllable.

### New layout library

Create a dedicated library project:

- `JitHub.Dashboard.Layouts` (name can be adjusted)
  - Pure UI layout logic: `VirtualizingLayout` implementation(s)
  - No app dependencies (so it’s testable)

This is the “official” successor of the old `WidgetLayout` prototype.

### Proposed layout types

1) `CardDeckLayout : VirtualizingLayout`
- Properties (dependency properties):
  - `MinColumns`, `MaxColumns`, `CardWidth`, `CardHeight`, `Spacing`
  - `DeckAngleStep`, `DeckOffset`, `DeckMaxVisibleCount`
  - `LayoutMode` (Auto / Grid / Deck)
- Behavior:
  - Auto chooses grid vs deck based on available width.
  - Switching modes must be a smooth animated morph (grid   deck) driven by resize.

2) `CardDeckPresenter` (control wrapper)
- Wraps `ItemsRepeater` + attaches animations.
- Owns the “programmatic swipe” API.

### Animation strategy (Uno-native)

Use a two-tier strategy:

1) **Baseline (guaranteed): Storyboard animations**
- Animate `Opacity` and `RenderTransform` using `CompositeTransform`.
- Keep transforms per-element (no shared transform instances; avoid reusing animations from resources across multiple controls).

2) **Enhanced (where Composition is available): Composition implicit animations**
- Use Composition visuals for smooth repositioning when layout changes.
- Prefer implicit animations for offset/rotation when items move because of resize or insertion.

Notes:
- Uno supports storyboarded animations broadly; GPU-friendly properties include opacity and basic transforms.
- Uno has a partial Composition API implementation. On Android, more features require API 29+.

### Programmatic swipe behavior

Expose an API that the viewmodel can invoke:

- `Task SwipeAsync(string cardId, SwipeDirection direction, CancellationToken ct)`

Implementation notes:
- Swipe is an animation (translate + rotate + fade).
- After swipe completes:
  - either remove the card from the list (if it represents a one-shot prompt)
  - or keep it and snap back (if it’s an action confirmation)
- The decision belongs to the provider/host via the card action semantics.

---

## Bottom compose box

### Requirements

- Centered at bottom.
- Floating (not touching the bottom edge).
- Works with safe areas on mobile.
- Does not fight the status bar placement.
- Looks and feels like a modern chat composer (ChatGPT-style): a soft surface, rounded corners, subtle elevation, and an input affordance.
- Behavior can be a no-op initially (no navigation/search results yet).

### Layout approach

- Compose box is an overlay layer placed above the main content.
- Use an `AutoLayout` container to layout input + submit affordance cleanly.
- Use responsive margins/padding for density tuning.

Behavior:
- On narrow screens, the compose remains bottom-centered; it must avoid overlap with system gestures and safe areas.
- On wide screens, keep max width (e.g., content width constraint) to maintain professional density.

---

## Status bar (desktop bottom vs mobile top)

The status bar is already implemented at Shell level.

### Requirement

- Desktop: bottom status bar (current behavior).
- Mobile/narrow: status bar moves to top and becomes more compact.

### Implementation approach

Modify `Shell.xaml` to become responsive:

- Use `ResponsiveView` or `VisualStateManager` to switch between:
  - Wide template: status at bottom
  - Narrow template: status at top

- Keep the same `StatusBarViewModel` bindings.
- For narrow template, reduce information density:
  - Hide non-critical fields (e.g., last updated label) using responsive visibility.

---

## Styling and theming

### Principles

- No hardcoded hex colors in XAML.
- Use `ThemeResource` and app-defined semantic resources.
- Use existing TextBlock styles (avoid explicit font sizes/weights in-line).
- Prefer Uno Toolkit + Material styles if already present (app has Toolkit + ThemeService features enabled).

### Resource structure

Add dedicated resource dictionaries for dashboard-specific styling:

- `JitHubV3/Themes/Dashboard.xaml`
  - Card surfaces, spacing tokens, corner radii (via resources)
  - Styles for card containers, sidebar items, compose box

App.xaml should merge these dictionaries.

---

## Accessibility and input

- Ensure tap targets meet mobile minimums.
- Apply `AutomationProperties.Name` to:
  - repo list items
  - card primary actions
  - compose text box and submit action

Keyboard:
- Compose box should support Enter-to-submit behavior (if applicable to the command semantics).

---

## Testing strategy (gates)

### Unit tests (layout engine)

In the new layout library test project:

- Deterministic layout math tests:
  - given available width/height, card count, and settings → expected rectangles
  - deck mode: angle/offset invariants
  - grid mode: column count selection and spacing

- Stability tests:
  - resizing from wide → narrow does not produce invalid (NaN/Infinity) positions

### App-layer unit tests

- Provider composition tests:
  - stable ordering
  - in-place sync (no duplicates; stable identity)

### UI tests

Using existing UI test infrastructure:
- Verify narrow vs wide layouts
- Verify sidebar collapses/opens
- Verify compose box stays visible and not overlapping status bar

Performance checks (lightweight):
- Ensure card host does not trigger full collection resets on refresh.

---

## Execution plan (incremental delivery)

This is a phased plan with validation gates.

Note: A much more detailed, implementation-ready plan is maintained in a separate document: `docs/dashboard-ui-execution-plan.md`.

### Phase A — Dashboard shell scaffolding
Deliverables:
- New `DashboardPage` + `DashboardViewModel` (replaces the current post-login page).
- Layout skeleton:
  - left sidebar placeholder
  - center card host placeholder (static cards)
  - compose box placeholder
- Responsive: narrow vs wide template switching.

Gate:
- Runs on desktop and narrow window sizes with correct placement.

### Phase B — Sidebar repo list integration
Deliverables:
- Reuse existing repo loading logic/service integration.
- Sidebar selection updates `DashboardContext.SelectedRepo`.

Gate:
- Cached-first repos show, refresh updates without flicker.

### Phase C — Card host + provider scaffolding
Deliverables:
- `IDashboardCardProvider` abstraction.
- One concrete provider (simple, e.g., “Assigned issues” or placeholder “Activity”).
- In-place sync update logic.

Gate:
- Card list updates smoothly on refresh.

### Phase D — Layout engine library (deck/grid)
Deliverables:
- New `JitHub.Dashboard.Layouts` project.
- `CardDeckLayout` with auto mode selection.
- Unit tests for layout math.

Gate:
- Layout tests passing; UI renders correctly across resize.

### Phase E — Animations and programmatic swipe
Deliverables:
- Baseline storyboard animations for enter/exit/reposition.
- Optional composition enhancements (guarded; fallback remains storyboard).
- Programmatic swipe API and one end-to-end use (e.g., star/skip on a sample card).

Gate:
- Swipe works on desktop; does not break on mobile.

### Phase F — Shell status bar responsiveness
Deliverables:
- Update `Shell.xaml` so status bar moves to top in narrow layout.
- Compact status bar template.

Gate:
- Verified status bar placement and readability at narrow widths.

---

## Acceptance criteria

- Dashboard is the first page after login.
- Left sidebar lists repositories and is responsive (persistent on wide, collapsible on narrow).
- Center card host adapts between grid/deck and responds smoothly to resize.
- Bottom compose box is centered and floating, respects safe areas, and does not overlap status bar.
- Status bar is bottom on desktop, top+compact on narrow/mobile.
- No hardcoded colors; styling is resource-driven.
- Layout engine is isolated, testable, and has unit test coverage.

---

## Decisions (locked for implementation)

- Sidebar uses `NavigationView`.
- Card host uses **grid** on “large enough” screens and **animates into a deck** as the window narrows.
- Compose box is a **ChatGPT-like natural-language composer** (initially no-op; wiring comes later).
