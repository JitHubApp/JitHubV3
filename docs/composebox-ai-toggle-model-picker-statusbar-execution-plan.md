# ComposeBox AI Toggle + Model/Settings Picker + Extensible Status Bar (Execution Plan)

This is the step-by-step execution plan for implementing the milestone described in:
- [docs/composebox-ai-toggle-model-picker-statusbar-milestone.md](composebox-ai-toggle-model-picker-statusbar-milestone.md)

Primary UX references to mimic:
- AI Dev Gallery README (product behavior goals like “browse, download, run models”, offline usage expectations, and device considerations):
  - [JitHub_old/ai-dev-gallery/README.md](../JitHub_old/ai-dev-gallery/README.md)
- AI Dev Gallery picker layout/interaction reference:
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Controls/ModelPicker/ModelOrApiPicker.xaml`

> Scope rule (important): implement exactly the milestone UX (toggle + gear dialog + info callout + composable status bar). Do not add extra pages, filters, or “nice-to-have” settings beyond what is required to make the picker functional.

---

## What exists today (so we build on it)

### Compose + selection + download plumbing
- ComposeBox currently includes a model dropdown + download UI in `DashboardPage.xaml`.
- Selection persistence exists via `IAiModelStore` (`AiModelSelection(RuntimeId, ModelId)`), implemented by `JsonFileAiModelStore`.
- Runtime resolution exists via `IAiRuntimeResolver` (`AiRuntimeResolver`).
- Local catalog + inventory exist via `IAiLocalModelCatalog` and `IAiLocalModelInventoryStore`.
- Download orchestration exists via `IAiModelDownloadQueue` / `AiModelDownloadQueue` with per-download subscription (`AiModelDownloadHandle.Subscribe`).

### Status bar plumbing
- Shell currently binds `StatusBarViewModel.Message` in `Shell.xaml`.
- `StatusBarViewModel` already supports safe UI-thread marshaling and extra fields (`IsBusy`, `IsRefreshing`, `Freshness`, `LastUpdatedAt`).

### AI runtime surface
- API runtimes exist: `OpenAiRuntime`, `AnthropicRuntime`, `AzureAiFoundryRuntime`, `LocalFoundryRuntime`.
- `IAiRuntimeCatalog` exists, currently implemented by `ConfiguredAiRuntimeCatalog` (only returns API runtimes when fully configured+keyed).

---

## Phase 0 — Guardrails and scaffolding (one-time groundwork)

### 0.1 Confirm current ComposeBox + download UI locations
- Confirm current UI elements and bindings in:
  - `JitHubV3/Presentation/DashboardPage.xaml`
  - `JitHubV3/Presentation/DashboardViewModel.cs`
- Confirm download progress binding shape:
  - properties currently used to show download progress + cancel in the compose area.

### 0.2 Decide hosting location for the overlay picker (Dashboard vs Shell)
Recommendation (implement first): host in Dashboard.
- Reason: the gear button is in ComposeBox which lives on the dashboard; hosting the overlay there avoids routing/visual-tree complexity.

Acceptance check:
- We can open/close the overlay without navigation side-effects.

### 0.3 Create folder skeleton for new UI components
Create these folders (if missing):
- `JitHubV3/Presentation/Controls/ModelPicker/`
- `JitHubV3/Presentation/Controls/StatusBar/` (if we add controls/templating)
- `JitHubV3/Presentation/Infrastructure/Events/` (if we add event types)

---

## Phase 1 — ComposeBox UX swap (toggle + gear + info callout)

Goal: remove the model dropdown from ComposeBox and replace it with:
- AI On/Off toggle
- Gear button to open picker overlay
- Info icon callout (“natural language search, not chat”)

### 1.1 Add AI enablement state (persisted)
We need a durable setting separate from `AiModelSelection`.

Implementation tasks:
- Add `IAiEnablementStore`:
  - `ValueTask<bool> GetIsEnabledAsync(CancellationToken ct)`
  - `ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct)`
- Add a JSON implementation stored under LocalAppData (mirror `JsonFileAiModelStore` conventions):
  - `JsonFileAiEnablementStore`
  - path like `%LocalAppData%/JitHubV3/ai/enablement.json`
- Register in DI in `App.xaml.cs`.

Tests:
- `JitHubV3.Tests`: unit tests for the store read/write behavior.

Acceptance checks:
- Toggle persists across app restarts.

### 1.2 Wire AI enablement into the compose orchestrator
Goal: AI should only be used when enabled.

Implementation tasks:
- Update `ComposeSearchOrchestrator` (or whatever component currently decides to call AI) to:
  - check `IAiEnablementStore`
  - if disabled: skip AI and proceed with non-AI/heuristic path
  - if enabled: attempt `IAiRuntimeResolver.ResolveSelectedRuntimeAsync`

Tests:
- Add/update tests similar to existing orchestrator AI tests:
  - When AI disabled: AI runtime is never invoked
  - When AI enabled but no selection: AI runtime is not invoked

Acceptance checks:
- Turning AI off reliably stops AI calls.

### 1.3 Replace dropdown UI in DashboardPage.xaml
Implementation tasks:
- In `DashboardPage.xaml`:
  - remove/hide the model dropdown (`ComboBox` bound to `AiModelOptions`)
  - remove embedded download controls tied to the dropdown selection
  - add:
    - a ToggleSwitch (or equivalent) bound to `IsAiEnabled`
    - a gear `Button` next to it (opens overlay)
    - an info `Button`/icon that opens a `TeachingTip` or `Flyout`

Notes:
- Keep visuals minimal and consistent with existing styles.
- Do not hardcode new colors; reuse resources.

ViewModel changes (`DashboardViewModel.cs`):
- Add properties/commands:
  - `bool IsAiEnabled` (loaded from `IAiEnablementStore`)
  - `ICommand ToggleAiEnabledCommand` (or setter triggers store write)
  - `ICommand OpenAiModelPickerCommand`

Acceptance checks:
- No dropdown visible.
- Toggle changes the effective compose behavior.
- Gear button opens a placeholder overlay (Phase 2 will add full UI).

### 1.4 Implement the info callout
Implementation tasks:
- Use `TeachingTip` (preferred) or `Flyout` anchored to the info button.
- Content must include:
  - A short explanation: “Natural language search (not chat). Your input is converted into GitHub queries.”
  - A short status line (initially static; later driven by status extensions): “AI: On/Off · Runtime/Model · HW”.

Acceptance checks:
- Callout opens and closes.
- No extra navigation occurs.

---

## Phase 2 — Overlay picker shell (structure and open/close)

Goal: create a modal overlay that mimics AI Dev Gallery’s `ModelOrApiPicker` layout:
- smoke overlay
- centered dialog
- left category list
- right content panel
- footer summary area
- Apply + Cancel

Reference: `ModelOrApiPicker.xaml` structure and interactions.

### 2.1 Create the overlay UserControl
Create:
- `JitHubV3/Presentation/Controls/ModelPicker/ModelOrApiPickerOverlay.xaml`
- `...Overlay.xaml.cs`

UI requirements (match reference at a high level):
- Root overlay Grid that covers the page
- “Smoke” background layer
- Dialog container centered with a max width/height and margins
- Header row with title + close button
- Left category selector (ListView)
- Right panel host (ContentControl or Grid)
- Footer with “selected summary” + Apply button

Behavior:
- Tapping the smoke background closes (optional; match reference: background tapped closes)
- Escape key closes (if reasonable)
- Focus trap/cycle within overlay (best-effort)

### 2.2 Add `ModelOrApiPickerViewModel`
Create:
- `JitHubV3/Presentation/Controls/ModelPicker/ModelOrApiPickerViewModel.cs`

Minimum properties/commands:
- `bool IsOpen`
- `ObservableCollection<ModelPickerCategoryItem> Categories`
- `ModelPickerCategoryItem? SelectedCategory`
- `object? ActiveCategoryViewModel` (or similar)
- `ICommand ApplyCommand`
- `ICommand CancelCommand`

Add models:
- `ModelPickerCategoryItem` with `Id`, `DisplayName`, maybe `Icon` (use existing assets or FontIcon)

### 2.3 Integrate overlay into DashboardPage
- Add the overlay control at the root of the Dashboard page grid so it can cover the full page.
- Bind its `Visibility` to `IsOpen`.
- Wire `OpenAiModelPickerCommand` to set `IsOpen = true`.

Acceptance checks:
- Overlay opens and closes.
- Layout matches the reference at a structural level.

---

## Phase 3 — Runtime catalog split (“declared” vs “available”) to support picker UX

Goal: the picker must show choices even when not fully configured (unlike today’s `ConfiguredAiRuntimeCatalog`).

### 3.1 Introduce declared runtime descriptor catalog
Add:
- `IAiRuntimeDescriptorCatalog` (or similarly named) that returns all known runtime descriptors regardless of configuration.
- Implementation `DefaultAiRuntimeDescriptorCatalog` returning at minimum:
  - OpenAI
  - Anthropic
  - Azure AI Foundry
  - Local Foundry

Descriptor must include requirement metadata:
- requires API key
- requires endpoint (Foundry)
- supports local downloads (Local Foundry)

### 3.2 Keep `IAiRuntimeCatalog` meaning “available now”
- Keep `ConfiguredAiRuntimeCatalog` but ensure it:
  - stays strict (only returns runtimes that can actually run now)
  - continues using secrets + effective config

Tests:
- Add tests proving declared list includes runtimes even without keys.

Acceptance checks:
- Picker can show API providers as selectable categories even before configured.

---

## Phase 4 — Runtime settings store (user-editable model id / endpoint)

Goal: the picker can modify runtime settings without editing appsettings.

### 4.1 Add runtime settings store
Add:
- `IAiRuntimeSettingsStore` storing non-secret settings:
  - OpenAI: ModelId
  - Anthropic: ModelId
  - Foundry: Endpoint + ModelId + ApiKeyHeaderName (optional; default to `api-key`)

Implementation:
- `JsonFileAiRuntimeSettingsStore` under LocalAppData (mirror other stores).

### 4.2 Define “effective config” resolution
Update runtime config construction:
- Combine:
  - base config from `IConfiguration`
  - overrides from `IAiRuntimeSettingsStore`

This may be implemented as:
- new helper `AiRuntimeEffectiveConfiguration` that returns effective configs for each runtime
- or update `OpenAiRuntimeConfig.FromConfiguration` patterns (prefer minimal churn)

Tests:
- Effective config resolves overrides correctly.

Acceptance checks:
- Picker changes (e.g., model id) actually affect runtime calls.

---

## Phase 5 — Local models category: selection + downloads in-picker

Goal: local model selection and download management move into the picker.

### 5.1 Category view model for local models
Create:
- `LocalModelsPickerViewModel`

Inputs/services:
- `IAiLocalModelCatalog`
- `IAiModelDownloadQueue`
- `IAiModelStore`

State:
- a list of local model options, each including:
  - model id, display name
  - is downloaded
  - download uri + artifact metadata (if available)
  - active download progress (if any)

Actions:
- Select a model
- Download model
- Cancel download

### 5.2 Hook into existing download queue
Implementation details:
- When user clicks Download:
  - build `AiModelDownloadRequest` using:
    - model id
    - runtime id (expected `local-foundry`)
    - source uri
    - install path (choose existing pattern already used in app)
    - optional artifact file name / sha / size
- Subscribe to download progress and update item VM.

### 5.3 Apply semantics for local models
- Apply sets `IAiModelStore.SetSelectionAsync(new AiModelSelection("local-foundry", selectedModelId))`.
- Apply should be disabled unless:
  - a selection exists
  - if selecting a local model: model is downloaded OR user explicitly triggers download first

Tests:
- Extend/keep existing `AiModelDownloadQueueTests` coverage
- Add picker-level unit tests for:
  - download progress updates reflected in item VM
  - cancel changes state

Acceptance checks:
- Download progress visible in picker.
- Cancel works.
- Apply selects the model.

AI Dev Gallery README alignment:
- Mirrors “browse, download, run models” and offline usage after download (see FAQ about offline use).

---

## Phase 6 — API provider categories: minimal settings + apply

Goal: configure API runtimes via the picker.

### 6.1 Provider category VMs
Create one VM per provider:
- `OpenAiPickerViewModel`
- `AnthropicPickerViewModel`
- `AzureAiFoundryPickerViewModel`

Each must expose:
- required fields (ModelId; Foundry also Endpoint)
- API key entry (stored via `ISecretStore`)
- validation state + Apply enabled/disabled

### 6.2 Persist settings and selection
Apply writes:
- provider settings -> `IAiRuntimeSettingsStore`
- api key -> `ISecretStore`
- selection -> `IAiModelStore`

Validation rules:
- OpenAI: model id + api key required
- Anthropic: model id + api key required
- Azure AI Foundry: endpoint + model id + api key required

### 6.3 Make `IAiRuntimeCatalog` reflect new settings
- Because the runtime catalog currently checks configuration+secrets, after Apply it should start returning the selected provider as “available now”.

Tests:
- Unit tests per provider VM:
  - Apply disabled when fields missing
  - Apply writes to stores

AI Dev Gallery README alignment:
- Preserves the idea that the app can run offline for local models, but API providers require network (implicit from API usage).

---

## Phase 7 — “Selected summary” footer behavior

Goal: mimic the reference footer area that shows selected model(s) summary and provides the primary Apply/Run action.

Implementation tasks:
- Add a “Selected” summary component in the picker footer:
  - show “No model selected” when none
  - show selected runtime + model label when selected
  - show hardware hint if known (Phase 9)

Acceptance checks:
- Footer always reflects current selection.
- Apply is the primary action.

---

## Phase 8 — Status bar extension/plugin architecture

Goal: replace single status message with composable segments.

### 8.1 Add core segment model + extension interface
Add:
- `StatusBarSegment` (Id, Text, IsVisible, Priority)
- `IStatusBarExtension` exposing segments and change notification
- `StatusBarComposer` singleton that:
  - gathers extensions via DI
  - produces ordered segments
  - updates `StatusBarViewModel` (new property `Segments` or similar)

Decision point:
- Either:
  - add `ObservableCollection<StatusBarSegment>` to `StatusBarViewModel`
  - or add a separate `StatusBarSegmentsViewModel` referenced by Shell

Recommended: add `Segments` collection to `StatusBarViewModel` to keep Shell bindings simple.

### 8.2 Update Shell.xaml rendering
- Replace the status `TextBlock` with an `ItemsRepeater` rendering segments inline.
- Keep `Message` as fallback (e.g., show message when there are no segments).

Acceptance checks:
- Status bar renders multiple segments.

---

## Phase 9 — Eventing for status updates (selection + download + enablement)

Goal: status updates are driven by explicit events.

### 9.1 Add a small event bus for AI status
Add:
- `IAiStatusEventBus` with `Subscribe(Action<AiStatusEvent>)`
- events:
  - `AiEnablementChanged`
  - `AiSelectionChanged`
  - `AiDownloadProgressChanged`

### 9.2 Publish events from stores/queues
Implementation tasks:
- Decorate `IAiModelStore` to publish `AiSelectionChanged` after successful set.
- Decorate `IAiEnablementStore` to publish `AiEnablementChanged`.
- Bridge downloads:
  - when the picker (or Dashboard) subscribes to an `AiModelDownloadHandle`, also publish `AiDownloadProgressChanged` to the bus

Acceptance checks:
- Status extensions update without direct coupling to picker VMs.

---

## Phase 10 — Implement first status bar extensions

### 10.1 AI status segment
Create `AiStatusBarExtension` that shows:
- `AI: On/Off`
- `Runtime: <runtimeId>`
- `Model: <modelId>`

Data sources:
- `IAiEnablementStore`
- `IAiModelStore`
- `IAiStatusEventBus`

### 10.2 Download status segment
Create `AiDownloadStatusBarExtension` that shows:
- Only when a download is active: `Downloading <model> (xx%)`

Data source:
- download progress events

### 10.3 Hardware status segment (minimal)
Create `HardwareStatusBarExtension` (capability-gated) that shows a conservative string:
- `HW: GPU` or `HW: CPU` (do not overpromise)

Acceptance checks:
- Segments appear and update as AI state changes.

---

## Phase 11 — Platform capability registry

Goal: isolate platform-specific behavior.

Implementation tasks:
- Add `IPlatformCapabilities`:
  - `SupportsSecureSecretStore`
  - `SupportsLocalFoundryDetection`
  - `SupportsHardwareAccelerationIntrospection`
- Default implementation returns conservative values.
- Windows partial can return true for secure secret store.

Acceptance checks:
- Picker hides/adjusts features when capabilities are false.

---

## Phase 12 — Cleanup: remove old ComposeBox model dropdown pathways

Goal: ensure there is a single source of truth.

Implementation tasks:
- Remove now-unused viewmodel properties related to the old dropdown and embedded download UI.
- Ensure model downloads are only initiated from the picker (unless there are other surfaces that must remain).
- Update any docs/comments.

Acceptance checks:
- App compiles cleanly.
- No dead bindings in XAML.

---

## Verification checklist (end-to-end)

### ComposeBox
- Toggle exists, persists, and gates AI usage.
- Gear opens picker.
- Info callout explains “natural language search (not chat)”.

### Picker
- Overlay behaves modally.
- Local models show downloaded/not-downloaded, can download with progress and cancel.
- API providers can be configured (model id + key; Foundry also endpoint).
- Apply is enabled only when selection is valid.

### Status bar
- Renders segments (AI state, selection, downloads, hardware if available).
- Updates via events (no tight coupling to picker VM).

### README reference alignment
- Supports the “browse/download/run models” mental model from AI Dev Gallery’s README.
- After downloading local models, app continues to work without internet (local path) similarly to the AI Dev Gallery FAQ statement.

---

## Suggested work breakdown for a coding agent (how we’ll execute)

This is the recommended order to keep PRs reviewable:

1) Phase 1 (toggle + gear + info) + Phase 0 scaffolding
2) Phase 2 overlay shell
3) Phase 3 declared/available split
4) Phase 4 settings store + effective config
5) Phase 5 local models + downloads in picker
6) Phase 6 API providers in picker
7) Phase 8–10 status bar extension system + segments
8) Phase 11 capabilities
9) Phase 12 cleanup
