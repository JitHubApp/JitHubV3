# JitHubV3 Dashboard UI — Execution Plan (Phased, Detailed)

This plan is derived from [docs/dashboard-ui-architecture.md](docs/dashboard-ui-architecture.md).

It is intentionally detailed and broken down into phases and sub-phases with explicit quality gates.

Guiding goals:
- Professional, sophisticated dev-tool UI.
- Fully responsive across desktop/tablet/mobile.
- Fluid, tasteful animations using Uno/WinUI-native primitives.
- High code quality: testable layout math, minimal coupling, strict resource-driven styling.

---

## Guiding principles (applies to every phase)

1) Resource-driven design
- No hardcoded color hex values in XAML.
- Use `ThemeResource` and app-level semantic resources.
- Add new values via ResourceDictionaries and (when needed) palette override files.

2) Responsive by construction
- Structural changes: `ResponsiveView` and/or `VisualStateManager`.
- Fine tuning: `utu:Responsive` markup extension for values (spacing, margins, visibility, widths).

3) Animation taste + safety
- Baseline animations use Storyboards animating GPU-friendly properties:
  - `Opacity`
  - `RenderTransform` with `CompositeTransform` (Translate/Scale/Rotate)
- No shared transform instances across elements.
- No reusing the same `Animation` instance across multiple controls (use templates).

4) Maintainability
- Keep the dashboard host stable while providers evolve.
- Encapsulate layout/animation complexities into dedicated controls/libraries.

5) Tests are the gate
- Layout math must be unit-testable.
- UI regression checks use existing UI test harness.

---

## Visual system specification (tokens, metrics, and styles)

This section defines the “taste” and the implementation mechanism.

### Spacing scale (numbers are allowed; they are metrics, not colors)

Use an 8-based scale with 4-step intermediates:
- `4, 8, 12, 16, 24, 32, 48, 64`

Define these as resources (example names):
- `DashboardSpacingXS = 4`
- `DashboardSpacingS = 8`
- `DashboardSpacingM = 12`
- `DashboardSpacingL = 16`
- `DashboardSpacingXL = 24`
- `DashboardSpacing2XL = 32`

### Corner radius

Define corner radius resources:
- `DashboardRadiusS = 8` (chips, compact surfaces)
- `DashboardRadiusM = 12` (cards)
- `DashboardRadiusL = 16` (compose box)

### Elevation/shadow

Use `ThemeShadow` for cross-platform consistent shadowing.

Define Z translation constants:
- `DashboardElevationCard = 8`
- `DashboardElevationCompose = 16`
- `DashboardElevationFloating = 24` (reserved; sparing use)

Implementation:
- Apply `ThemeShadow` on focal surfaces (cards/compose) and set `Translation="0,0,Z"`.

### Color and surfaces (semantic, resource-based)

Do not specify raw color codes. Instead specify which semantic brushes are used and where.

- App background: `ApplicationPageBackgroundThemeBrush`
- Card surface: `CardBackgroundFillColorDefaultBrush`
- Card border (if needed): `CardStrokeColorDefaultBrush`
- Primary text: default `TextBlock` foreground (theme-controlled)
- Secondary text: theme resource for subtle text (use existing Material/Fluent resources as present)

Where a missing semantic brush is identified, add it to an app ResourceDictionary (not inline). If the app uses Material toolkit resources, prefer those.

### Typography

- Do not set font size/weight inline.
- Use existing `TextBlock` styles (`TitleTextBlockStyle`, etc.) already defined in the app/toolkit.
- Ensure information hierarchy via style selection and spacing, not ad-hoc sizes.

### Motion spec (timings and easing)

Use consistent timing to feel “professional”:

- Micro transitions (hover, subtle emphasis): `120–160ms`
- Layout transitions (grid↔deck morph on resize): `240–320ms`
- Swipe (programmatic left/right): `240–360ms`

Easing:
- Prefer `CubicEase` / `QuadraticEase` (EaseOut) for most motion.
- Avoid bouncy easing for a dev-tool aesthetic.

Properties animated:
- `Opacity`
- `CompositeTransform.TranslateX/TranslateY`
- `CompositeTransform.Rotation` (deck angle)
- `CompositeTransform.ScaleX/ScaleY` (optional subtle emphasis)

---

## Phase 0 — Inventory and design alignment

### 0.1 Confirm design system inputs
Deliverables:
- Confirm whether Material resources are active and preferred (app uses `UnoFeatures` including `Toolkit` and `ThemeService`; ensure chosen theme is consistent).
- Identify existing resource dictionaries and where to place `Dashboard.xaml`.

Gate:
- A single source of truth for dashboard tokens/resources is agreed.

### 0.2 Define the responsive ruleset
Deliverables:
- Define the threshold for “large enough” that switches grid ↔ deck.
- Decide whether threshold is:
  - Uno Toolkit breakpoints (Narrow/Normal/Wide), or
  - explicit `ResponsiveLayout` numeric values for the dashboard content.

Gate:
- Threshold documented and referenced by the host control.

---

## Phase 1 — Dashboard page scaffolding (no providers yet)

### 1.1 Add dashboard page + viewmodel
Deliverables:
- `DashboardPage.xaml` + `DashboardPage.xaml.cs` (use `ActivatablePage` pattern)
- `DashboardViewModel.cs`
- Update login navigation to go to `DashboardViewModel` instead of `MainViewModel`.

Gate:
- App navigates to dashboard after login.

### 1.2 Layout skeleton (responsive)
Deliverables:
- Root layout uses `ResponsiveView` (two templates minimum):
  - Wide template:
    - `NavigationView` left pane visible
    - status bar at bottom (Shell)
    - compose box bottom-center overlay
    - center content area (placeholder)
  - Narrow template:
    - `NavigationView` collapses pane (hamburger)
    - status bar moved to top (Shell update later, but dashboard must not conflict)
    - center content stacks correctly

Gate:
- Resizing window flips wide/narrow template with no layout break.

### 1.3 Compose box UI (ChatGPT-like, no-op)
Deliverables:
- A bottom-centered floating surface with:
  - multi-line `TextBox` (placeholder text like “Ask anything…”)
  - trailing icon button placeholder (disabled)
  - subtle elevation and rounded corners
- Responsive width:
  - Narrow: nearly full width with safe margins
  - Wide: max width constraint (professional density)

Gate:
- Looks correct on narrow and wide; no functional wiring required.

---

## Phase 2 — Shell status bar responsiveness

### 2.1 Add narrow vs wide status bar templates to Shell
Deliverables:
- Update `Shell.xaml` using `ResponsiveView` or `VisualStateManager`:
  - Wide: existing bottom status bar layout
  - Narrow: top compact status bar

Compact spec:
- Keep busy indicator + primary message.
- Hide last updated label and/or freshness label on Narrowest if cramped.

Gate:
- Status bar relocates correctly on resize.

---

## Phase 3 — Sidebar (NavigationView repo list) integration

### 3.1 NavigationView pane content
Deliverables:
- Implement repo list as the `NavigationView.PaneContent`.
- Use existing repo service and cache-first behavior.
- Selection model:
  - repo selection updates `DashboardContext.SelectedRepo`
  - (optional) selection also navigates to repo pages via card actions, not direct selection

Gate:
- Repos load and refresh without flicker.

### 3.2 Sidebar styling
Deliverables:
- Sidebar item template with:
  - owner/name hierarchy
  - subtle secondary text for description (when available)
  - consistent density across breakpoints

Gate:
- Sidebar looks “dev-tool professional” and does not feel like a raw ListView.

---

## Phase 4 — Card model + provider system (host stability first)

### 4.1 Define card abstraction
Deliverables:
- `DashboardCardModel` (id, kind, title, subtitle/summary, actions)
- `IDashboardCardProvider` interface
- `DashboardContext`

Gate:
- Provider interface is stable and testable.

### 4.2 Host composition and ordering
Deliverables:
- `DashboardViewModel` composes multiple providers.
- In-place sync (no collection resets) using the existing `ObservableCollection` sync helper.
- Error-handling and cancellation aligned with caching/polling patterns.

Gate:
- Dummy provider updates do not cause flicker.

---

## Phase 5 — Layout engine library (grid↔deck morph)

This is the core technical investment.

### 5.1 Create new library project
Deliverables:
- `JitHub.Dashboard.Layouts` project
- `JitHub.Dashboard.Layouts.Tests` project (NUnit + FluentAssertions)

Gate:
- Builds cross-platform (no platform-only dependencies).

### 5.2 Implement layout math
Deliverables:
- `CardDeckLayout : VirtualizingLayout`
  - Inputs: available size, item count, card metrics
  - Outputs: item rects
- Modes:
  - Grid mode (large enough)
  - Deck mode (narrow)
  - Auto mode selects based on width threshold

Deck layout spec:
- Only top N cards get full fidelity; the rest can be stacked deeper.
- Each card in deck gets:
  - a slight rotation offset (small angles)
  - a small translate offset (y stacking)
  - an optional scale taper (very subtle, optional)

Grid layout spec:
- Column count determined by width and `CardMinWidth` + spacing.
- Ensure consistent gutters and alignment.

Gate:
- Unit tests cover deterministic rect generation for multiple widths.

### 5.3 Morph animation: grid↔deck on resize
Deliverables:
- `CardDeckPresenter` wrapper that:
  - hosts `ItemsRepeater`
  - detects mode changes and triggers animations

Animation approach:
- Baseline: storyboard per realized element animating:
  - transform to new translation/rotation
  - opacity if items appear/disappear
- Keep transforms per element; no shared resources.

Gate:
- Resizing window causes a smooth morph, not a rebuild.

---

## Phase 6 — Card UI visuals (surfaces, density, hierarchy)

### 6.1 Card container style
Deliverables:
- `DashboardCardContainerStyle` in `Themes/Dashboard.xaml`:
  - background uses card surface theme brush
  - corner radius uses dashboard radius resource
  - padding uses spacing resources
  - optional border uses semantic stroke brush
  - elevation uses ThemeShadow + translation

Gate:
- Cards look consistent on desktop and mobile.

### 6.2 Card templates
Deliverables:
- Base card template with:
  - title line
  - optional subtitle/metadata row
  - body/summary region
  - action row (buttons/links)

Gate:
- At least one provider card looks polished.

---

## Phase 7 — Programmatic swipe (optional early, but planned)

### 7.1 Swipe API and animations
Deliverables:
- `SwipeAsync(cardId, direction)` on presenter
- Storyboard:
  - translate off-screen in direction
  - rotate slightly in direction
  - fade out

### 7.2 Integrate one sample action
Deliverables:
- A sample card that triggers a swipe (e.g., star/skip concept).

Gate:
- Swipe runs cross-platform without relying on unsupported APIs.

---

## Phase 8 — Tests, QA, and performance gates

### 8.1 Layout unit tests
Deliverables:
- grid invariants
- deck invariants
- mode switching invariants
- resize stability (no NaN/Infinity)

### 8.2 UI tests (existing UITest project)
Deliverables:
- narrow vs wide layout verification
- compose box present and not overlapping
- sidebar open/close behavior on narrow

### 8.3 Performance checks
Deliverables:
- Ensure no collection clears on refresh.
- Verify virtualization keeps memory stable with many cards.

---

## Definition of Done (for the dashboard milestone)

- Dashboard is first page after login.
- NavigationView sidebar is responsive and polished.
- Card host morphs grid↔deck smoothly on resize.
- Compose box looks like a modern chat composer (no-op is acceptable).
- Status bar relocates top/bottom correctly by breakpoint.
- Styles are entirely resource-driven; no hardcoded colors.
- Layout engine is isolated, unit-tested, and reusable.
