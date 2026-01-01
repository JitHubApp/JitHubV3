# Milestone — ComposeBox AI Toggle + Model/Settings Dialog + Extensible Status Bar

This document captures the next milestone after the initial ComposeBox + AI query builder work:

1) Replace the ComposeBox model dropdown with an **AI on/off toggle** and a **gear button**.
2) The gear opens a **Model/Settings dialog** that closely mimics AI Dev Gallery’s `ModelOrApiPicker` behavior.
3) Add an **info icon callout** next to the ComposeBox explaining that this is **natural language search** (not chat).
4) Upgrade the Shell status bar into an **extension/plugin architecture** that renders runtime/provider/hardware state and other runtime indicators, driven by events.

> Scope rule: this is an architecture + staged plan doc. No code changes are made here.

---

## Goals (what “done” means)

### ComposeBox UX
- The ComposeBox no longer exposes a model dropdown.
- The ComposeBox exposes:
  - an **AI toggle** (`On`/`Off`)
  - a **gear button** that opens a model/settings picker
  - an **info icon** that explains the feature and the current runtime at a glance

### Model/Settings dialog UX
- The dialog behaves like AI Dev Gallery’s picker:
  - “smoke” overlay behind a centered dialog
  - left “source/type” list (local models vs API providers)
  - main panel that changes based on selected source/type
  - selected model summary area (chips/summary) + explicit Save/Apply action
  - local model download state & progress, cancel, and errors

### Status bar extensibility
- The status bar becomes a composable surface driven by extensions.
- Extensions can:
  - contribute segments (e.g., `AI: On`, `Runtime: local-foundry`, `Model: qwen2.5-0.5b`, `HW: GPU`)
  - react to runtime events (selection changes, download progress, etc.)
- Shell renders the composed segments in both narrow/wide layouts.

---

## Current repo state (what we already have)

### ComposeBox model selection + downloads exist (but as a dropdown)
- `DashboardPage.xaml` currently includes a `ComboBox` bound to `AiModelOptions` / `SelectedAiModel`, plus Download/Cancel UI and a progress area.

### Download backend already exists
- `IAiModelDownloadQueue` + `AiModelDownloadQueue` implement:
  - enqueue download requests (`AiModelDownloadRequest`)
  - per-download progress subscription (`AiModelDownloadHandle.Subscribe(...)`)
  - cancellation
  - inventory persistence via `IAiLocalModelInventoryStore`

### Local model inventory & catalog already exist
- `IAiLocalModelInventoryStore` persists installed/downloaded model entries.
- `IAiLocalModelCatalog` returns catalog items with `IsDownloaded` + optional `InstallPath`.
- `FoundryLocalModelCatalogDecorator` augments the catalog with models discovered from local Foundry.

### Runtime selection already exists
- `IAiModelStore` persists `AiModelSelection(RuntimeId, ModelId)`.
- `IAiRuntimeResolver` resolves the selected runtime implementation.

### API runtimes exist (but currently “only if configured”)
- `ConfiguredAiRuntimeCatalog` currently reports API runtimes only when:
  - model id exists in config, AND
  - API key exists in `ISecretStore`

This behavior is correct for “what can run now”, but it blocks an AI Dev Gallery–style picker which must also show “available but not configured” choices.

### Shell status bar exists (but is a single message)
- `StatusBarViewModel` provides `Message`, `IsBusy`, `IsRefreshing`, `Freshness`, `LastUpdatedAt`.
- `Shell.xaml` displays `StatusBar.Message` for both narrow/wide layouts.

---

## Target reference UX (AI Dev Gallery)

We will heavily mimic the behavior and layout style of:
- `JitHub_old/ai-dev-gallery/AIDevGallery/Controls/ModelPicker/ModelOrApiPicker.xaml`

Key behaviors we want to match:
- Modal overlay + centered dialog
- Left-side category selector
- Right-side details area
- Explicit apply/save action
- Visible “selected model(s)” summary
- Download progress + cancel surfaced where selection happens

---

## Gaps (what we want vs what we have)

### ComposeBox
- Today: “choose model” is a dropdown embedded in the ComposeBox.
- Goal: ComposeBox is **not a model picker**; it’s a search box. Model selection lives behind the gear.

### Picker UX
- Today: there is no dialog/overlay that resembles `ModelOrApiPicker`.
- Goal: a single picker that includes:
  - local model selection
  - download management
  - API provider selection + configuration affordances

### API provider configuration
- Today: `OpenAiRuntimeConfig` / `AnthropicRuntimeConfig` / `AzureAiFoundryRuntimeConfig` are loaded from `IConfiguration`.
- Goal: user-facing “settings” in the picker (at minimum: API key + model id; Foundry also needs endpoint + header name rules).

### Status bar is not extensible
- Today: a single string.
- Goal: a composable bar with multiple independent contributors.

---

## Proposed architecture

### 1) Split “declared runtimes” from “available runtimes”

We need two related concepts:

- **Declared runtimes**: what we can *offer* in UI (even if not configured yet)
- **Available runtimes**: what we can *run now* (dependencies + configuration + secrets satisfied)

Proposed interfaces:

- `IAiRuntimeDescriptorCatalog`
  - returns all known runtime descriptors (OpenAI, Anthropic, Azure AI Foundry, Local Foundry)
  - includes requirements metadata (requires API key? requires endpoint? supports local downloads?)

- Keep existing `IAiRuntimeCatalog`
  - continues to answer “what can run now?”
  - used for runtime resolution/validation and for enabling/disabling the Apply action

> This is the key move that enables an AI Dev Gallery–style picker while preserving the correctness of “available”.

### 2) Add a user-writable runtime settings store (override appsettings)

Because appsettings are not a good runtime-edit surface, we need a persisted settings layer.

Proposed:
- `IAiRuntimeSettingsStore`
  - stores non-secret runtime settings (model ids, endpoints, preferred defaults)
  - stored under LocalAppData (json) similarly to `JsonFileAiModelStore`

Keep secrets in `ISecretStore`:
- OpenAI API key
- Anthropic API key
- Azure AI Foundry API key

Config resolution approach:
- Runtime config objects should become “effective config”:
  - base from `IConfiguration` (appsettings)
  - overridden by `IAiRuntimeSettingsStore` values (user settings)

This lets the picker write settings without mutating appsettings.

### 3) Model picker dialog composition

We implement a dedicated picker surface under `JitHubV3/Presentation/Controls/ModelPicker/`.

Recommended approach (to mimic reference overlay closely):
- A `UserControl` overlay that:
  - is hosted in the Dashboard page (or Shell) root grid
  - toggles `Visibility` for modal open/close
  - draws its own “smoke” background
  - contains the dialog UI as a centered Grid

Why not only `ContentDialog`?
- `ContentDialog` is fine for simple dialogs, but the reference UX is a bespoke overlay with side selector + footer “chips”. A page-hosted overlay control is closer to the reference and keeps layout fully controllable.

ViewModels:
- `ModelOrApiPickerViewModel`
  - `ObservableCollection<ModelPickerCategoryItem>` left list
  - `SelectedCategory`
  - `ObservableCollection<ModelPickerOptionItem>` main list (or a dedicated VM per category)
  - `SelectedOption`
  - `SelectedSummary` (what’s currently chosen)
  - `ApplyCommand` + `CancelCommand`

Data sources:
- local models: `IAiLocalModelCatalog`
- downloads: `IAiModelDownloadQueue`
- current selection: `IAiModelStore`
- API providers: `IAiRuntimeDescriptorCatalog` + `IAiRuntimeSettingsStore` + `ISecretStore`

Apply semantics:
- Apply writes:
  - selection to `IAiModelStore` (`AiModelSelection`)
  - any provider settings to `IAiRuntimeSettingsStore`
  - any API key updates to `ISecretStore`

### 4) Centralize “AI enabled” toggle

We should treat “AI enabled” as an app/user setting separate from the model selection.

Proposed:
- `IAiEnablementStore` (or fold into `IAiRuntimeSettingsStore`)
  - stores `IsAiEnabled`

Orchestrator behavior:
- `ComposeSearchOrchestrator` uses AI only when:
  - `IsAiEnabled` is true
  - a valid runtime is resolvable and available

This aligns with the UX requirement: toggle is a *global switch*.

### 5) Status bar extension/plugin architecture

We keep `StatusBarViewModel` as the Shell’s binding root, but evolve it into a composed surface.

Proposed types:

- `StatusBarSegment`
  - `Id` (stable)
  - `Text`
  - `IsVisible`
  - `Priority`

- `IStatusBarExtension`
  - exposes `IReadOnlyList<StatusBarSegment>` (or an `ObservableCollection`)
  - raises `Changed` when its segments change

- `StatusBarComposer`
  - created once (singleton)
  - discovers all `IStatusBarExtension` via DI
  - builds a flattened ordered segment list
  - updates `StatusBarViewModel` on change

Shell rendering:
- Replace the single `TextBlock` with an `ItemsRepeater` rendering segments inline.
- Keep `StatusBarViewModel.Message` as a fallback (compat and simple ad-hoc messages).

Eventing model:
- Prefer a small, explicit event bus per domain rather than a “global message bus”.
- For AI/runtime status:
  - publish selection changes (when `IAiModelStore.SetSelectionAsync` is called)
  - publish download changes (bridge from `IAiModelDownloadQueue` progress)
  - publish “runtime availability” changes if we decide to poll/detect (e.g., Foundry on PATH)

Implementation strategy for selection events:
- Wrap `IAiModelStore` with a decorator that publishes an event after successful `SetSelectionAsync`.

Core status extensions (first wave):
- `AiStatusBarExtension`: shows AI enablement + runtime + model id.
- `AiDownloadStatusBarExtension`: shows “Downloading <model> (xx%)” when active.
- `HardwareStatusBarExtension`: shows CPU/GPU status (see capability registry below).

### 6) Platform capability registry

Some status (and some picker affordances) will be platform-dependent.

Proposed:
- `IPlatformCapabilities`
  - `bool SupportsSecureSecretStore`
  - `bool SupportsLocalFoundryDetection`
  - `bool SupportsHardwareAccelerationIntrospection`
  - etc.

Implementation pattern:
- Provide a default cross-platform implementation + platform-specific partials (e.g., `.Windows.cs`) similar to `PasswordVaultSecretStore.Windows.cs`.

The picker/status bar can use capabilities to:
- show/hide hardware detail segments
- show/hide “install Foundry” guidance
- choose the correct secret store backing

---

## UX details (minimum required)

### ComposeBox callout (info icon)
- An info icon next to the ComposeBox.
- Clicking opens a short explanation:
  - “This is natural-language search (not chat). We convert your text into GitHub queries.”
  - Show a short status line: “AI: On/Off · Runtime/Model · HW” (sourced from the same status extensions).

Implementation notes:
- Use `TeachingTip` or a `Flyout` attached to the info button.
- Keep the text short and action-oriented.

---

## Staged implementation plan

### Stage 1 — ComposeBox UX swap (no dialog yet)
- Replace the model `ComboBox` with:
  - AI toggle
  - gear button (disabled or opens placeholder)
  - info icon callout
- Ensure ComposeBox submit still works when AI is off.

Acceptance checks:
- No model dropdown visible.
- AI toggle state visibly affects whether AI is used.

### Stage 2 — Build the overlay dialog shell (structure only)
- Implement the overlay + header + side list + footer + apply/cancel.
- Wire open/close from gear button.

Acceptance checks:
- Dialog opens/closes and traps interaction (modal overlay behavior).

### Stage 3 — Local models selection + download management
- Populate local models via `IAiLocalModelCatalog`.
- Add Download/Cancel buttons per model using `IAiModelDownloadQueue`.
- Show progress and errors.
- Apply updates `IAiModelStore` selection.

Acceptance checks:
- Starting a download updates progress UI.
- Cancelling works.
- Selecting an already downloaded model applies immediately.

### Stage 4 — API provider selection + settings
- Add categories for OpenAI/Anthropic/Azure AI Foundry.
- Add minimal settings UI per provider:
  - model id
  - (Foundry) endpoint
  - API key (stored via `ISecretStore`)
- Apply persists settings and selection.

Acceptance checks:
- Provider selection is possible without pre-editing appsettings.
- Missing required fields disable Apply with a clear inline message.

### Stage 5 — Status bar extension system
- Introduce segment-based status bar rendering.
- Add the first extensions:
  - AI selection/enablement segment
  - download segment
  - hardware segment (capability-gated)

Acceptance checks:
- Status bar shows segments and updates when selection/download changes.

---

## Risks / open questions

- **Where to host the overlay**: Dashboard vs Shell. Shell-hosting makes it global, Dashboard-hosting is simpler for the ComposeBox gear.
- **Hardware detection**: decide what “hardware state” means cross-platform (GPU present? ONNX EP? Foundry uses what?). Start with a conservative string.
- **Settings precedence**: define clear override rules between appsettings and user settings.

---

## Related docs

- `docs/composebox-github-search-and-ai-architecture.md`
- `docs/composebox-github-search-and-ai-execution-plan.md`
