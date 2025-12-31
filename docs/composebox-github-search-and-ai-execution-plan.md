
# ComposeBox — GitHub Search + AI Query Builder (Execution Plan)

This is the execution plan for implementing the ComposeBox feature described in:
- [docs/composebox-github-search-and-ai-architecture.md](composebox-github-search-and-ai-architecture.md)

This plan intentionally references (conceptually and structurally) the AI Dev Gallery reference project included in this repo:
- [JitHub_old/ai-dev-gallery/README.md](../JitHub_old/ai-dev-gallery/README.md)

And it is grounded in official Uno Platform guidance where it affects cross-platform implementation details (configuration, auth, secure storage, protocol activation, etc.).

---

## How to read this plan

- The architecture doc is the **source of truth** for layering and responsibilities.
- This doc is the **source of truth** for sequencing, deliverables, and definition-of-done.
- Each phase includes:
	- Deliverables (code/doc)
	- Key design constraints (from architecture)
	- Reference mapping to AI Dev Gallery (what pattern we’re mirroring)
	- Uno considerations (what to validate using official docs)
	- Validation (tests/manual checks)

---

## Scope and non-goals

### In scope (per architecture)

- ComposeBox becomes a GitHub search entrypoint supporting:
	- structured GitHub query syntax forwarded to GitHub Search endpoints
	- plain-language queries translated to structured queries via an AI layer
- Multi-domain search (issues/PRs, repos, code, users) runs in parallel and returns grouped results.
- Results are presented using **dashboard cards** with a clean adapter/factory abstraction.
- Multiple AI runtimes are supported (API-key providers first; local models later; Foundry guided setup).

### Explicit non-goals (for initial phases)

- No new page-based “search results” experience (cards only).
- No broad “chat assistant” or repo-content summarization.
- No model fine-tuning work until the late phase.

---

## Key references (must stay aligned)

### Canonical architecture

- [docs/composebox-github-search-and-ai-architecture.md](composebox-github-search-and-ai-architecture.md)

### Reference implementation (use heavily)

AI Dev Gallery concepts we must mirror (conceptually, not verbatim):

- “browse models → download models → run samples → export projects” mindset
- model catalog (available vs downloaded)
- download queue with progress + cancellation
- persistent store of downloaded inventory
- model picker UI separating local vs API-based runtimes

Key artifacts referenced in the architecture doc:

- Download orchestration: `AIDevGallery/Utils/ModelDownloadQueue.cs`, `AIDevGallery/Utils/ModelDownload.cs`
- Cache/inventory persistence: `AIDevGallery/Utils/ModelCacheStore.cs`
- Model selection UI: `AIDevGallery/Controls/ModelPicker/*`
- Provider/config metadata: `AIDevGallery/Models/GenAIConfig.cs`

### Uno documentation touchpoints (validate during execution)

Use Uno docs search to confirm the most current guidance when implementing each area:

- Configuration: embedded `appsettings.json` patterns (Uno.Extensions.Configuration)
- HTTP: endpoint registration / HttpClientFactory patterns (Uno.Extensions.Http)
- Authentication and protocol activation:
	- WebAuthenticationBroker
	- custom protocol activation (iOS/macOS Info.plist, Android exported activity, WASM limitations)
- Secure storage: Uno.Extensions.Storage and platform-specific requirements (Keychain entitlements on Apple platforms)

---

## Phase 0 — ComposeBox foundation (non-AI, single-domain)

Primary goal: ship a minimal end-to-end path where ComposeBox input triggers GitHub issue/PR search and displays results via dashboard cards.

This phase is the foundation that later phases build on, and it must preserve patterns already established in the repo:

- service abstractions in GitHub.Abstractions
- cached wrappers in GitHub.Octokit
- stable cache keys
- dashboard cards and stable CardId rules

### 0.1 UI binding and command wiring

**Deliverables**

- ComposeBox input bound to a viewmodel property (e.g., `ComposeText`).
- Send button enabled/disabled based on input + busy state.
- A submit command (e.g., `SubmitComposeCommand`) and cancellation behavior.

**Constraints (from architecture)**

- Keep [docs/composebox-github-search-and-ai-architecture.md](composebox-github-search-and-ai-architecture.md) contract: UI should bind to VM; orchestration happens outside the view.

**AI Dev Gallery mapping**

- Conceptually matches how AI Dev Gallery samples are triggered via a thin UI that dispatches work into a model/runtime layer.

**Uno validation**

- Ensure bindings and command patterns match how the app currently wires viewmodels.

**Validation**

- Manual: ComposeBox send enables/disables as expected, input preserved across navigation.

### 0.2 Minimal orchestrator (single-domain: issues/PRs)

**Deliverables**

- `IComposeSearchOrchestrator` implementation with a narrow `SearchAsync`.
- Structured query detection (simple heuristics):
	- If it contains GitHub qualifiers (`repo:`, `org:`, `is:` etc.) treat as structured
	- Otherwise treat as plain text but (for Phase 0) forward as-is (no AI yet)
- Use existing issue search service (`IGitHubIssueSearchService`) and existing refresh patterns.

**Constraints**

- Respect cache-first responsiveness requirements (cache-only or prefer-cache-then-refresh).
- Cancellation: orchestrator must accept `CancellationToken` and stop work.

**AI Dev Gallery mapping**

- Mirrors the “runtime execution entrypoint” idea: one place that accepts input, chooses a path, and produces a result.

**Uno validation**

- Ensure async cancellation does not deadlock UI thread.

**Validation**

- Unit test: orchestrator routes to issue search.
- Unit test: cancellation stops provider execution.

### 0.3 Results presentation via dashboard cards (single group)

**Deliverables**

- A “search results card adapter/factory layer” that takes orchestrator output and produces dashboard card models.
- Initial card(s):
	- “Search Results (Issues/PRs)” group card
	- Each result item rendered as a card (or a list card), consistent with existing dashboard patterns

**Constraints**

- CardId uniqueness and stability must be preserved.
- Keep orchestrator UI-agnostic: orchestrator returns a model that can be adapted to cards.

**AI Dev Gallery mapping**

- Similar to “results view” in samples: execution produces a structured output displayed in a consistent UI.

**Validation**

- Manual: results appear as cards and update deterministically when query changes.

---

## Phase 1 — Multi-domain search (parallel) with grouped results (still non-AI)

Primary goal: expand to repos/code/users and run them in parallel, returning grouped results that map cleanly to dashboard cards.

### 1.1 Domain services in GitHub.Abstractions

**Deliverables**

- Define domain-specific search service abstractions:
	- `IGitHubRepoSearchService`
	- `IGitHubCodeSearchService`
	- `IGitHubUserSearchService`
- Define query records for each domain, mirroring the existing issue query pattern.

**Constraints**

- Keep the same architecture layering as issue search.

**AI Dev Gallery mapping**

- Similar separation of “provider interfaces” from “runtime implementations”.

**Validation**

- Unit tests for query normalization (where applicable).

### 1.2 Octokit implementations + cache wrappers + cache keys

**Deliverables**

- Octokit-backed implementation for each domain.
- Cached wrappers that follow cache-first policy.
- Stable cache keys in the same style as existing search keys.

**Constraints**

- Cache key must include: domain + normalized query + paging/sort.

**Validation**

- Unit tests that cache key generation is stable and collision-free.

### 1.3 Orchestrator parallel execution + grouping

**Deliverables**

- Orchestrator runs requested domains in parallel.
- Produces `ComposeSearchResponse` grouped by domain.
- Introduce a deterministic ordering of groups.

**Constraints**

- Must remain deterministic without AI.
- Must gracefully handle partial failures (e.g., code search fails but issues succeed).

**AI Dev Gallery mapping**

- Mirrors “multi-source availability” concept: multiple model sources are queried; UI shows what’s available.

**Validation**

- Unit tests: parallel execution returns groups and is cancelable.

### 1.4 Card presentation for grouped results

**Deliverables**

- For each domain, a minimal card representation:
	- Issues/PRs group
	- Repos group
	- Code group
	- Users group
- Ensure the adapter/factory layer stays the only place that knows about card types.

**Validation**

- Manual: groups are visible and clearly separated.

---

## Phase 2 — AI query builder (API-key providers first, cross-platform baseline)

Primary goal: enable a constrained AI step that converts natural language into structured GitHub query syntax and selects domain(s).

This phase establishes the “mature middle layer” described in the architecture doc, but starts with API providers only.

### 2.1 Define AI middle-layer abstractions

**Deliverables**

- `IAiRuntimeCatalog`: reports available runtimes on the current platform.
- `IAiRuntime`: a single constrained function to build GitHub search queries.
- `IAiModelStore`: persist selected runtime + model id.
- `IAiSecretStore` (or reuse existing secret abstraction): store API keys securely.

**Constraints**

- Output schema must be constrained: `query`, `domain` (or multiple domains), optional explanation.
- Treat AI output as untrusted: validate and clamp.

**AI Dev Gallery mapping**

- Mirrors: catalog + selection + persistence.
- Use the same mental model as AI Dev Gallery: “providers + models are selectable; some require downloads; some require keys”.

**Uno validation**

- Validate secure storage approach using Uno.Extensions.Storage guidance (Apple Keychain requirements).

**Validation**

- Unit tests for schema validation and clamping.

### 2.2 Implement API-key runtimes: OpenAI, Anthropic, Azure AI Foundry

**Deliverables**

- Three `IAiRuntime` implementations:
	- OpenAI
	- Anthropic
	- Azure AI Foundry
- Provider configuration model:
	- endpoint
	- model id
	- request options (max tokens, temperature if needed)

**Constraints**

- Never log secrets.
- Minimize data sent: only the ComposeBox input and minimal context.

**AI Dev Gallery mapping**

- Mirrors “API models” portion of AI Dev Gallery (providers defined as configs, selectable at runtime).

**Uno validation**

- Confirm Uno.Extensions.Configuration embedding patterns for `appsettings.json`-driven defaults.
- Confirm Uno.Extensions.Http guidance if using HttpClientFactory registration.

**Validation**

- Unit test: runtime executes and returns constrained schema.

### 2.3 Wire AI step into orchestrator (optional and capability-based)

**Deliverables**

- Orchestrator decides:
	- if input is structured, bypass AI
	- if input is plain language and AI runtime available/enabled, call AI
	- otherwise proceed with non-AI search behavior

**Constraints**

- Deterministic when AI is disabled.
- Always allow the final structured query to be shown/edited (as per architecture intent).

**Validation**

- Manual: toggling AI on/off changes behavior predictably.

---

## Phase 3 — Model catalog + download queue + model picker (mirror AI Dev Gallery)

Primary goal: build the “AI Dev Gallery-like” experience for local model acquisition and selection.

This phase is Windows-first, but the abstractions must remain cross-platform.

### 3.1 Model catalog representation and persistence

**Deliverables**

- A catalog of local models/runtimes:
	- which models exist
	- whether downloaded
	- where stored
	- which runtime can execute them
- Persistent inventory store (similar to AI Dev Gallery cache store concept).

**AI Dev Gallery mapping**

- Mirror `ModelCacheStore` concept: a persistent index (e.g., JSON) listing downloaded models.

**Uno validation**

- Validate storage directories and secure vs non-secure storage.

**Validation**

- Unit tests: inventory survives restart; corruption handling.

### 3.2 Download queue with progress + cancellation

**Deliverables**

- A download queue service:
	- enqueue model download
	- observe progress
	- cancel download
	- persist incomplete state if required

**AI Dev Gallery mapping**

- Mirror `ModelDownloadQueue` and `ModelDownload` concepts:
	- queue-based orchestration
	- progress reporting
	- cancel support
	- integrity checks where feasible

**Validation**

- Manual: enqueue/download/cancel flows.

### 3.3 Model picker UI (runtime selection)

**Deliverables**

- A UI surface to pick provider/model:
	- local models
	- API providers
- Persist selection via `IAiModelStore`.

**AI Dev Gallery mapping**

- Mirror the “model picker” concept and UX philosophy from the AI Dev Gallery README: an approachable selection experience that powers execution.

**Validation**

- Manual: selection changes affect ComposeBox behavior.

---

## Phase 4 — Local Foundry guided setup (Windows-first, additive)

Primary goal: add a runtime provider that detects and uses local Foundry where available, plus guided setup UI.

### 4.1 Foundry runtime provider

**Deliverables**

- `IAiRuntime` implementation that:
	- detects Foundry availability
	- enumerates runnable models
	- executes query-building locally

**AI Dev Gallery mapping**

- AI Dev Gallery README emphasizes local model execution and model selection as a first-class experience.

**Validation**

- Manual: detect and run on supported Windows machines.

### 4.2 Guided setup UX

**Deliverables**

- A minimal guidance surface that helps the user set up Foundry locally.

**Constraints**

- Keep UX minimal and aligned with existing app structure.

---

## Phase 5 — Fine-tuning (late stage, optional runtime)

Primary goal: ship an optional small model specialized for “natural language → GitHub query syntax”.

### 5.1 Dataset + evaluation harness

**Deliverables**

- A dataset of:
	- input text
	- expected GitHub query
	- expected domain(s)
- An evaluation harness producing:
	- normalized exact-match metrics
	- domain classification accuracy
	- safety checks (bounded output, no secret leakage)

**Constraints (from architecture)**

- Strict output schema and validation remain mandatory.

### 5.2 Runtime integration

**Deliverables**

- A local runtime path (where supported) and fallback to API.

---

## Phase 6 — Platform-specific runtime implementations (late stage)

Primary goal: expand local runtime support across platforms without breaking interfaces.

### 6.1 Windows

- Local GPU/CPU runtimes and built-in model paths where feasible.

### 6.2 macOS

- Local Foundry (where available) and compatible local runtimes.

### 6.3 iOS / Android

- API-first.
- Later explore local runtimes if they can be hosted idiomatically in .NET/Uno.

### 6.4 Linux (Skia)

- Explore ONNX-based local models on CPU and GPU.
- Keep this additive: do not block earlier phases.

**Uno validation**

- Confirm Skia Desktop target constraints and packaging considerations.

---

## Cross-cutting workstreams (run alongside phases)

### A) Observability and diagnostics

- Structured logging around:
	- search request lifecycle
	- provider execution times
	- AI runtime selection and failures (without secrets)

### B) Security and privacy

- Secret handling:
	- never logged
	- stored securely via the app’s secret store
- Data minimization for API providers.

### C) Performance and caching

- Cache keys stable and collision-free.
- Consider TTL policy later; initial implementation uses existing cache runtime semantics.

### D) Tests

- Unit tests:
	- cache keys
	- orchestrator grouping and cancellation
	- AI schema validation
- UI tests:
	- basic ComposeBox submit flow

---

## Milestones and “definition of done”

### Milestone 0 (Phase 0 complete)

- ComposeBox submit triggers issue search and displays results as dashboard cards.
- Cancellation works.

### Milestone 1 (Phase 1 complete)

- Multi-domain parallel search returns grouped results and renders as grouped cards.

### Milestone 2 (Phase 2 complete)

- AI query builder works cross-platform via API-key providers (OpenAI/Anthropic/Azure AI Foundry) with secure key storage.

### Milestone 3 (Phase 3–4 complete)

- Model catalog + download queue + model picker (mirroring AI Dev Gallery patterns) and Foundry guidance on Windows.

### Milestone 4 (Phase 5–6 optional)

- Fine-tuned runtime option + broader platform local runtime coverage.

