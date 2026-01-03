# JitHubV3 vs AI Dev Gallery — Model/API Picker Parity Gap Report

> Goal: make JitHubV3’s “AI model selection + download” experience look and behave **almost exactly** like AI Dev Gallery’s picker, with an implementation roadmap grounded in actual source.
>
> Scope: differences in **UX**, **XAML structure**, **styling/resources**, **view composition architecture**, **model/catalog & selection semantics**, and **download/caching pipeline**.

---

## 0) Quick read (what’s most different)

### The 5 biggest parity gaps
1. **Multi-model selection UX (footer chips + “Run sample”) is missing**
   - AI Dev Gallery supports selecting multiple models for a sample, shows them as removable chips in the footer, and emits `SelectedModelsChanged`.
   - JitHubV3 is single-selection (one runtime + one model), has only a text summary + `Apply/Cancel`.

2. **Picker composition is plugin-like in AI Dev Gallery; category switching is static MVVM in JitHubV3**
   - AI Dev Gallery dynamically selects and loads picker “views” via `ModelPickerDefinition.Definitions` and per-view availability checks.
   - JitHubV3 uses fixed categories wired in `ModelOrApiPickerViewModel` (`local-models`, `openai`, `anthropic`, `azure-ai-foundry`) and swaps content using a `DataTemplateSelector`.

3. **ONNX/local picker surface is vastly richer in AI Dev Gallery**
   - AI Dev Gallery splits models into **Available / Downloadable / Unavailable**, has empty states, context menus, “add model” workflows, and deep metadata.
   - JitHubV3 local picker is a single `ListView` of catalog items with `Download/Cancel` and a `ProgressBar`.

4. **Styling system is centralized in AI Dev Gallery; JitHubV3 currently has almost no component-level style resources**
   - AI Dev Gallery merges many ResourceDictionaries (`Styles/Button.xaml`, `Styles/Card.xaml`, `Styles/Colors.xaml`, etc.) and uses dedicated button styles (`SubtleButtonStyle`, `TertiaryButtonStyle`, `AccentButtonStyle`) and gradients.
   - JitHubV3 merges only metric dictionaries (`Presentation/Themes/Shell.xaml`, `Presentation/Themes/Dashboard.xaml`) and relies mostly on default WinUI styles.

5. **Download pipeline responsibilities differ**
   - AI Dev Gallery’s download queue is tightly integrated with a persistent **model cache** and UI/global “downloads list” control; it also supports verification-failure UX and notifications.
   - JitHubV3’s download queue is a robust artifact fetch + SHA256 + zip extraction + inventory persistence, but it is not integrated into a global “download progress list” UI, and does not model “verifying/verification failed” as first-class UX.

---

## 1) Source-of-truth file map

### AI Dev Gallery (reference implementation in this repo)
- Overlay control: `JitHub_old/ai-dev-gallery/AIDevGallery/Controls/ModelPicker/ModelOrApiPicker.xaml` and `.xaml.cs`
- Picker plugin registry: `JitHub_old/ai-dev-gallery/AIDevGallery/Controls/ModelPicker/ModelPickerViews/ModelPickerDefinitions.cs`
- Rich ONNX picker view: `JitHub_old/ai-dev-gallery/AIDevGallery/Controls/ModelPicker/ModelPickerViews/OnnxPickerView.xaml` and `.xaml.cs`
- Download queue + caching integration:
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Utils/ModelDownloadQueue.cs`
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Utils/ModelCache.cs`
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Utils/ModelCacheStore.cs`
- Download VM wrapper: `JitHub_old/ai-dev-gallery/AIDevGallery/ViewModels/DownloadableModel.cs`
- Style system root: `JitHub_old/ai-dev-gallery/AIDevGallery/App.xaml`
- Style dictionaries (selected):
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Styles/Button.xaml`
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Styles/Colors.xaml`
  - `JitHub_old/ai-dev-gallery/AIDevGallery/Styles/Card.xaml`

### JitHubV3 (current implementation)
- Overlay control: `JitHubV3/Presentation/Controls/ModelPicker/ModelOrApiPickerOverlay.xaml` and `.xaml.cs`
- Overlay VM: `JitHubV3/Presentation/Controls/ModelPicker/ModelOrApiPickerViewModel.cs`
- Local models picker VM: `JitHubV3/Presentation/Controls/ModelPicker/LocalModelsPickerViewModel.cs`
- Download queue: `JitHubV3/Services/Ai/AiModelDownloadQueue.cs`
- Local model definitions from config: `JitHubV3/Services/Ai/AiLocalModelDefinitionsConfiguration.cs`
- App resources (merged dictionaries): `JitHubV3/App.xaml`
- Tokens/metrics: `JitHubV3/Presentation/Themes/Dashboard.xaml`

---

## 2) UX + interaction gaps (behavioral parity)

### 2.1 Modal overlay behavior
**AI Dev Gallery**
- Modal overlay with a smoke layer and a centered card-like dialog.
- Has explicit open/close semantics in code-behind (`Show/Hide`) and emits a `Closed` event.
- On open, it programmatically sets focus (e.g., `CancelButton.Focus(FocusState.Programmatic)` in `ModelOrApiPicker.Show(...)`).
- Often includes implicit show/hide animations (see `AIDevGallery/App.xaml` `DefaultShowAnimationsSet` / `DefaultHideAnimationsSet`).

**JitHubV3**
- Modal overlay is a `UserControl` whose root `Grid.Visibility` binds to `IsOpen` (`ModelOrApiPickerOverlay.xaml`).
- Dismiss via full-screen transparent button bound to `CancelCommand`.
- No explicit animation resources wired for show/hide.
- No explicit focus management is performed when opening.

**Gap**
- The *perceived* modal behavior is similar, but AI Dev Gallery is more “productized”: animations, explicit lifecycle events, and focus handling patterns (AI Dev Gallery uses more code-behind orchestration).

**Work required**
- Add optional show/hide animation resources and wire them to the overlay container.
- Decide whether JitHubV3 keeps MVVM-only overlay or introduces a control API similar to AI Dev Gallery (events + show/hide methods).
- Add focus behavior parity (e.g., focus the close/cancel action on open, restore focus on close).

### 2.2 Left rail (picker view list) and availability
**AI Dev Gallery**
- Left rail is a list of “picker views” with icons. It can collapse via visual states if only one picker is available.
- Availability is computed per view (`IsAvailable()` checks), and only available pickers are shown.
- Picker entries come from a definition registry (`ModelPickerDefinition.Definitions`) with `Name`, `Id`, `Icon`, `CreatePicker`, and optional async `IsAvailable`.

**JitHubV3**
- Left rail is a `ListView` bound to `Categories` from `ModelOrApiPickerViewModel`.
- Categories are static: always includes Local models; includes OpenAI/Anthropic/Foundry only if `IPlatformCapabilities.SupportsSecureSecretStore`.

**Gap**
- AI Dev Gallery’s picker list is **dynamic by model type + runtime availability**, not just platform capability.
- JitHubV3 categories map to “provider settings editors” as first-class categories, while AI Dev Gallery categories map to “model sources / runtimes / APIs” relevant to *what sample needs*.
- AI Dev Gallery can include pickers that are only conditionally available at runtime (e.g., `OllamaModelProvider.Instance.IsAvailable`, `LemonadeModelProvider.Instance.IsAvailable`).

**Work required**
- Introduce a “picker definition” registry (or equivalent) that:
  - filters based on required model types and current platform/runtime capabilities
  - can collapse the left rail when only one picker is available

### 2.3 Multi-selection (repeater-driven) model selection flow
**AI Dev Gallery**
- The picker supports selecting multiple models, not just one.
- `ModelOrApiPicker.Load(...)` accepts `List<List<ModelType>> modelOrApiTypes` and constructs a `ModelSelectionItem` per entry.
- It preselects defaults per “slot” using:
  - an `initialModelToLoad` argument, and/or
  - usage history (`App.AppData.UsageHistoryV2`) including optional hardware accelerator matching.

**JitHubV3**
- The picker is single-selection: `IAiModelStore` stores `AiModelSelection(RuntimeId, ModelId)`.
- `LocalModelsPickerViewModel` best-effort restores selection only when the persisted selection has `RuntimeId == "local-foundry"`.

**Gap**
- JitHubV3 cannot express “N required model choices for this context/sample”.
- Preselection heuristics are minimal in JitHubV3 compared to AI Dev Gallery.

**Work required**
- Define a multi-selection model (even if only internally to the picker), including:
  - selection slots / required model types per slot
  - preselection rules (history, accelerator preference)
  - serialization strategy (if persistent)

### 2.3 Footer: selected models chip list and primary action semantics
**AI Dev Gallery**
- Footer presents “Models selected for this sample” and an `ItemsRepeater` of selected models as chips/cards.
- Primary action is “Run sample”. Picker is effectively a **pre-flight** step before running.
- Emits selected models list changes and/or returns selected models.

**JitHubV3**
- Footer is a single summary text (`FooterSummary`) + `Cancel` + `Apply`.
- Apply persists selection to `IAiModelStore` and closes.

**Gap**
- AI Dev Gallery is designed for **sample execution**, not just selecting a single model. The footer is a core affordance.

**Work required**
- Define whether JitHubV3 should:
  - keep “Apply” semantics, OR
  - adopt “Run sample” semantics in contexts where the picker is invoked.
- Add multi-selection data model (selected models list) and chip UI.

### 2.4 Picker set: additional runtimes/providers that don’t exist in JitHubV3
AI Dev Gallery ships picker views for (folder contents verified):
- `WinAIApiPickerView`
- `FoundryLocalPickerView`
- `OnnxPickerView` (“Custom models”)
- `OllamaPickerView` (availability-gated)
- `OpenAIPickerView`
- `LemonadePickerView` (availability-gated)

JitHubV3 currently has categories/view-models for:
- local models (via `IAiLocalModelCatalog`)
- OpenAI / Anthropic / Azure AI Foundry *settings editors*

**Gap**
- There is no equivalent of “Windows AI APIs”, “Ollama”, or “Lemonade” pickers in JitHubV3.
- Even where a provider name overlaps (“OpenAI”), the UI shape differs (picker vs settings editor).

---

## 3) XAML structure gaps (layout + control composition)

### 3.1 Smoke layer
**AI Dev Gallery**
- Uses a dedicated smoke brush (often a semi-transparent dark overlay).

**JitHubV3**
- Uses `Border Background="{ThemeResource ApplicationPageBackgroundThemeBrush}" Opacity="0.72"` in `ModelOrApiPickerOverlay.xaml`.

**Gap**
- The “smoke” tint differs; AI Dev Gallery’s overlay reads as a distinct dimmer layer.

**Work required**
- Add a dedicated smoke brush resource (preferably theme-resource derived) and use it consistently across overlays.

### 3.2 Dialog container geometry
**AI Dev Gallery**
- Typical constraints: `MaxWidth="786"`, `MaxHeight="640"`, corner radius via `OverlayCornerRadius`, and subtle elevation via `ThemeShadow` + `Translation`.

**JitHubV3**
- `MaxWidth="900"`, `MaxHeight="720"`, corner radius `{StaticResource DashboardRadiusL}`, shadow via `ThemeShadow` only.

**Gap**
- Size/geometry will not “feel” identical.

**Work required**
- Align max size and corner radius to match AI Dev Gallery (or intentionally diverge, but then it will never be “almost exactly”).

### 3.3 Right panel surface treatment (gradient/card)
**AI Dev Gallery**
- The picker uses a gradient brush in the content area (e.g. `CardGradient2Brush` is referenced by `ModelOrApiPicker.xaml`).
- The gradient resources live in `AIDevGallery/Styles/Colors.xaml` and are theme-specific.

**JitHubV3**
- The right panel uses `Background="{ThemeResource LayerFillColorDefaultBrush}"` and border strokes.

**Gap**
- Without `CardGradient2Brush` (and related surface styling), the picker content area will not match AI Dev Gallery’s look.

### 3.3 Category list item visuals
**AI Dev Gallery**
- Category items include icons and typically use custom styles for selection states.

**JitHubV3**
- Category items are a plain `TextBlock` template.

**Gap**
- Missing iconography, spacing, and selection visuals.

**Work required**
- Introduce an item template with icon + text and consistent padding.

### 3.4 Local/ONNX model list visual language
**AI Dev Gallery (OnnxPickerView)**
- Uses modern “settings-card” list rows:
  - `ItemsView` + `ItemContainer`
  - `CommunityToolkit.WinUI.Controls.SettingsCard` as the row visual
  - a per-row “More options” button with a `MenuFlyout`
- Provides empty-state UI when there are no downloaded models (“No models downloaded”).
- Shows per-model metadata:
  - file size
  - source icon
  - hardware accelerator badges rendered as small tertiary buttons with explanatory flyouts
- Provides rich per-model actions in the context menu (examples seen in XAML):
  - “View model card”
  - “View license”
  - “Copy as path”
  - “Open containing folder”
  - “Delete”

**JitHubV3 (LocalModelsTemplate in ModelOrApiPickerOverlay.xaml)**
- Uses a plain `ListView` with a `Grid` row template.
- Shows only:
  - display name
  - a one-line status
  - progress bar
  - `Download` / `Cancel` buttons
- No empty state, no per-model metadata, no context menu actions.

**Gap**
- Even if you match the overlay shell and left rail, the local model picker content will still look and feel fundamentally different unless you adopt AI Dev Gallery’s card/list idiom.

---

## 4) Styling/resource system gaps (the “why it doesn’t look the same” section)

### 4.1 ResourceDictionary strategy
**AI Dev Gallery**
- `AIDevGallery/App.xaml` merges many dictionaries: `Styles/Button.xaml`, `Styles/Colors.xaml`, `Styles/Card.xaml`, etc.
- Defines theme dictionaries for gradient brushes and assets.
- Defines implicit animation sets (`DefaultShowAnimationsSet`, `DefaultHideAnimationsSet`).

**JitHubV3**
- `JitHubV3/App.xaml` merges only:
  - WinUI controls resources
  - Uno Toolkit resources
  - two metric-only dictionaries (`Presentation/Themes/Shell.xaml`, `Presentation/Themes/Dashboard.xaml`)
- Defines a small set of tint brushes that are built from WinUI theme resources.

**Gap**
- AI Dev Gallery’s UI consistency comes from a comprehensive style layer; JitHubV3’s picker currently inherits default WinUI visuals.

**Work required**
- Add dedicated style dictionaries for:
  - overlay/container styles
  - button styles (Accent/Subtle/Tertiary)
  - list item selection visuals
  - “chip” visuals
- Wire these dictionaries in `JitHubV3/App.xaml`.

### 4.2 Implicit animations
**AI Dev Gallery**
- Defines `DefaultShowAnimationsSet` and `DefaultHideAnimationsSet` in `AIDevGallery/App.xaml` and applies them when loading picker views (e.g. `Implicit.SetShowAnimations(modelPickerView, ...)`).

**JitHubV3**
- No equivalent implicit animation resources are defined/used for the picker.

**Gap**
- Even if layout is matched, the picker will still “feel different” without the same motion design.

### 4.2 Button styles
**AI Dev Gallery**
- Uses explicit button styles like `TertiaryButtonStyle` (see `AIDevGallery/Styles/Button.xaml`).
- Many controls reference `SubtleButtonStyle`, `AccentButtonStyle`, `TertiaryButtonStyle`.

**JitHubV3**
- Uses default `Button` styling.

**Gap**
- A major visual difference: AI Dev Gallery’s picker relies on subtle, tertiary, and accent button treatments.

**Work required**
- Define and adopt the same style keys in JitHubV3 (or map to existing tokens).

### 4.3 Gradients + custom colors
**AI Dev Gallery**
- Defines `CardGradient2Brush` and others in `AIDevGallery/Styles/Colors.xaml` using hard-coded gradient stops.

**JitHubV3**
- Does not define these; uses theme brushes such as `CardBackgroundFillColorDefaultBrush` and `LayerFillColorDefaultBrush`.

**Gap**
- Without those gradients, AI Dev Gallery’s right panel header/background treatments cannot be matched.

**Important constraint to note**
- In JitHubV3’s current design approach, we have avoided hard-coded hex colors (using `ThemeResource` instead). AI Dev Gallery uses hard-coded gradient colors.

**Work required**
- Decide one of:
  - Accept hard-coded colors for parity (breaks current JitHubV3 design constraint), OR
  - Recreate a similar gradient effect using theme-derived colors (will be “close” but not identical).

### 4.4 Theme assets (icons)
**AI Dev Gallery**
- `ModelPickerDefinition` entries reference explicit icon asset paths like:
  - `ms-appx:///Assets/ModelIcons/WCRAPI.png`
  - `ms-appx:///Assets/ModelIcons/Foundry.png`
  - `ms-appx:///Assets/ModelIcons/OpenAI{AppUtils.GetThemeAssetSuffix()}.png`

**JitHubV3**
- Category list is text-only; there are no per-category icon assets.

**Gap**
- Missing iconography is a primary “looks different” signal.

### 4.4 Overlay corner radius key
**AI Dev Gallery**
- Many styles use `OverlayCornerRadius`.

**JitHubV3**
- Uses `DashboardRadiusL/M`.

**Gap**
- Parity requires either introducing `OverlayCornerRadius` in JitHubV3 or remapping usage.

---

## 5) View composition + code architecture gaps

### 5.1 Control-driven vs MVVM-driven picker
**AI Dev Gallery**
- `ModelOrApiPicker` is largely orchestrated via code-behind.
- It dynamically:
  - chooses which picker views to show
  - loads models
  - preselects models based on sample needs and prior usage
  - emits model selection change events

**JitHubV3**
- `ModelOrApiPickerOverlay` is MVVM with `ModelOrApiPickerViewModel`.
- Category switching selects one `PickerCategoryViewModel` instance and uses templates.
- Persistence is via `IAiModelStore`.

**Gap**
- AI Dev Gallery’s picker is **context-aware** and can be invoked with requirements (“this sample needs these model types”).
- JitHubV3’s picker is currently **context-free** and global.

**Work required**
- Add an invocation contract for JitHubV3 picker (inputs/outputs), e.g.:
  - required model types, required capabilities, max selection count, etc.
  - output selected models list

### 5.2 Base picker view contract
**AI Dev Gallery**
- Picker views share a base contract (`BaseModelPickerView`):
  - `Task Load(List<ModelType> types)`
  - `void SelectModel(ModelDetails? modelDetails)`
  - `event SelectedModelChanged`

**JitHubV3**
- There is no equivalent view contract; JitHubV3’s right panel content is a set of view models bound through templates.

**Gap**
- AI Dev Gallery’s host can treat all picker views uniformly and swap them dynamically. JitHubV3 can’t without introducing an abstraction.

### 5.2 Static categories vs definition registry
**AI Dev Gallery**
- Has a definition list (`ModelPickerDefinition.Definitions`) and selects among `OnnxPickerView`, `OpenAIPickerView`, `OllamaPickerView`, etc.

**JitHubV3**
- Categories are hard-coded in `ModelOrApiPickerViewModel` and are tied to provider settings editors.

**Gap**
- There is no plugin/definition system in JitHubV3, so it cannot match AI Dev Gallery’s “sidebar list of picker views” model.

**Work required**
- Introduce a model similar to:
  - `PickerDefinition { Id, Title, Icon, IsAvailableAsync, ViewModelFactory/ViewFactory }`
  - and a host that loads the selected definition’s view.

---

## 6) Model data / selection semantics gaps

### 6.1 Single selection vs multi selection
**AI Dev Gallery**
- Selection is a list (for a sample). Footer shows selected models.

**JitHubV3**
- `IAiModelStore` persists one `AiModelSelection(RuntimeId, ModelId)`.
- `LocalModelsPickerViewModel.CanApply` enforces downloaded-only single item selection.

**Gap**
- Multi selection is not representable in current `IAiModelStore` shape.

**Work required**
- Add multi-selection store or extend selection model.

### 6.2 “Provider configuration editor” vs “picker of runnable options”
**AI Dev Gallery**
- OpenAI picker is typically about selecting/configuring a provider for running a sample.

**JitHubV3**
- OpenAI/Anthropic/Foundry categories are direct settings editors (model id, api key, endpoint, header name).

**Gap**
- AI Dev Gallery’s picker *feels like choosing a runnable path*, not editing settings.

**Work required**
- Reframe provider panels to match AI Dev Gallery’s content hierarchy and visual density (more card-like, less “form”).

### 6.3 Catalog/source model differences (what data the picker can even show)
**AI Dev Gallery**
- The picker builds a model list from multiple sources:
  - built-in `ModelDetailsHelper.GetModelDetailsForModelType(...)`
  - external catalogs (`ExternalModelHelper.GetAllModelsAsync()`)
- It uses URL schemes/prefixes to route behavior:
  - `ModelDownloadQueue` chooses `FoundryLocalModelDownload` when `modelDetails.Url` starts with `fl:`; otherwise uses `OnnxModelDownload`.
- It filters ONNX models to cached-only in some contexts (see `ModelOrApiPicker.Load(...)` where ONNX models are included only if `App.ModelCache.IsModelCached(m.Url)`).

**JitHubV3**
- Local models are driven by:
  - a catalog service (`IAiLocalModelCatalog`) for what’s available/installed
  - optional configuration-driven download metadata (`AiLocalModelDefinitionsConfiguration` reads `Ai:LocalModels` and supplies `DownloadUri`, `ExpectedBytes`, `ExpectedSha256`, etc.)
- There is no equivalent concept of “external model catalog aggregation” used by the picker.

**Gap**
- JitHubV3 cannot show AI Dev Gallery’s breadth of model sources without additional catalog/provider layers.

---

## 7) Downloading + progress UX gaps

### 7.1 Download state model
**AI Dev Gallery**
- `DownloadableModel` wraps a `ModelDownload` and exposes:
  - `DownloadStatus` including `Waiting`, `InProgress`, `Verifying`, `VerificationFailed`, `Completed`, `Canceled`
  - a `VerificationFailureMessage`
  - progress via a timer (updates UI every 300ms)

**JitHubV3**
- `AiModelDownloadQueue` publishes `AiModelDownloadProgress` with:
  - `Queued`, `Downloading`, `Completed`, `Canceled`, `Failed`
  - optional `Progress` (0..1) and bytes
  - no explicit “verifying” phase, though it does compute SHA256 and may extract zip

**Gap**
- AI Dev Gallery’s UX explicitly surfaces verification as a stage; JitHubV3 treats verification failures as a generic failure.

**Work required**
- Add a “verifying” stage to `AiModelDownloadProgress` (or emulate with events) if parity is desired.
- Add structured error messaging for SHA mismatch and extraction failures.

### 7.2 Queue semantics and dedupe
**AI Dev Gallery**
- `ModelDownloadQueue.AddModel(...)` returns `null` if already cached (`App.ModelCache.IsModelCached(modelDetails.Url)`).
- Dedupes by URL: if a download exists for the URL, returns the existing download.
- Processes downloads sequentially and dispatches the download start onto the UI thread (`App.MainWindow.DispatcherQueue.TryEnqueue(...)`).
- Emits `ModelsChanged` and `ModelDownloadCompleted` events.
- Sends a Windows App Notification on completion (`Microsoft.Windows.AppNotifications`).

**JitHubV3**
- `AiModelDownloadQueue.Enqueue(...)` always creates a new `Guid` handle and enqueues work; there is no “already cached” fast-path.
- It overwrites an existing artifact file if present and updates an inventory store entry.
- Emits `DownloadsChanged` and provides per-handle progress subscriptions.

**Gap**
- JitHubV3 lacks URL-based dedupe and “already cached” no-op behavior.
- JitHubV3 lacks OS-level completion notifications.

**Work required**
- Decide whether dedupe should be by `(ModelId, RuntimeId)` or `SourceUri`.
- Add an inventory/cached check to avoid redundant downloads.
- Add optional completion notification mechanism (platform dependent).

### 7.2 Global download list UI
**AI Dev Gallery**
- Has `DownloadProgressList` control and integrates with download queue.

**JitHubV3**
- Download progress is shown inline per list item in the local picker.

**Gap**
- Missing “global downloads list” surface, which is a recognizable AI Dev Gallery pattern.

---

## 8) Conventions and patterns differences

### 8.1 “App singletons” vs DI-first
**AI Dev Gallery**
- Common usage pattern is `App.ModelCache`, `App.ModelDownloadQueue`, `App.AppData`, `App.MainWindow`.

**JitHubV3**
- Uses dependency injection/service injection patterns into VMs (`IAiModelStore`, `IAiModelDownloadQueue`, `IAiLocalModelCatalog`, `IAiRuntimeSettingsStore`, etc.).

**Gap**
- Porting AI Dev Gallery logic 1:1 will require adapting from app-singletons to injected services (or intentionally introducing app-level singletons, which would diverge from JitHubV3’s current conventions).

### 8.2 Eventing model
**AI Dev Gallery**
- Uses direct events (`SelectedModelsChanged`, `Closed`, `ModelsChanged`, `ModelDownloadCompleted`).

**JitHubV3**
- Uses a mix of events (`DownloadsChanged`) and an app-level event publisher (`IAiStatusEventPublisher`) for status bar extensions.

**Gap**
- A parity port will need a consistent eventing strategy so UI and status surfaces stay in sync.

---

## 9) Concrete parity checklist (by file in JitHubV3)

### 9.1 Overlay UI structure
Target file: `JitHubV3/Presentation/Controls/ModelPicker/ModelOrApiPickerOverlay.xaml`
- Add left-rail item template parity: icon + title + selection visuals
- Replace footer summary-only with:
  - “Models selected…” label
  - selected-model chips (ItemsRepeater)
  - primary action semantics alignment (possibly “Run sample”)
- Add empty-state UX in right panel for pickers with no data

### 9.2 View model responsibilities
Target file: `JitHubV3/Presentation/Controls/ModelPicker/ModelOrApiPickerViewModel.cs`
- Add a picker-definition registry concept (or equivalent) to match AI Dev Gallery composition.
- Add invocation contract inputs (required model types, etc.) and output list.

### 9.3 Local models surface
Target file: `JitHubV3/Presentation/Controls/ModelPicker/LocalModelsPickerViewModel.cs`
- Consider splitting local items into groups like:
  - available downloaded models
  - downloadable models
  - unavailable models (incompatible)
- Add AI Dev Gallery-style empty state (“No models downloaded”).
- Add “more options” per item (context menu) actions if parity is desired:
  - view model card
  - view license
  - copy as path
  - open containing folder
  - delete
- Add “Add model” affordances (AI Dev Gallery exposes add-model UI in the ONNX picker).

### 9.4 Downloads
Target file: `JitHubV3/Services/Ai/AiModelDownloadQueue.cs`
- Add explicit “verifying” stage if parity is required.
- Add structured failure reporting so UI can show “Verification failed” vs generic “Failed”.

---

## 10) Notes on design constraints (important)

AI Dev Gallery’s visual system relies on hard-coded gradient stops and bespoke style dictionaries (see `AIDevGallery/Styles/Colors.xaml` and `AIDevGallery/App.xaml`).

JitHubV3 has been intentionally built with:
- metric-only token dictionaries (`Presentation/Themes/Dashboard.xaml`)
- theme-resource-derived brushes (no custom hex colors)

If strict “almost exactly the same” parity is required, **this constraint will need to be relaxed**, or you will need to accept that gradients/colors can only be approximated via theme resources.

---

## 11) Recommended implementation order (to reduce churn)
1. **Define the parity target contract**: single vs multi model selection, “Apply” vs “Run sample”, required model types.
2. **Introduce the definition registry (composition)** so the left rail + right panel behave like AI Dev Gallery.
3. **Rebuild the footer** (chips + primary action) because it drives most of the control semantics.
4. **Rework the local picker** to match AI Dev Gallery’s sections + actions.
5. **Bring in styling dictionaries** last, once the control tree matches, to avoid rewriting styles multiple times.
