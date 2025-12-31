# ComposeBox — GitHub Search + AI Query Builder (Architecture)

This document describes the architecture for turning the Dashboard ComposeBox into a **GitHub search** entrypoint that supports:

- **Direct GitHub query syntax** (advanced/structured queries)
- **Plain-language queries** (translated into GitHub query syntax via a small, purpose-built local/remote model)
- Multiple **search domains** (issues/PRs, repositories, code, users, …)
- Multiple **AI runtimes** depending on what’s available on the user’s machine

> Scope note: This doc focuses on architecture and staged implementation. It does not prescribe final UX beyond the existing ComposeBox and the user’s requirement to expose configuration via API and UI.

---

## Current repo state (relevant anchors)

### ComposeBox is currently UI-only
- ComposeBox is defined in `JitHubV3/Presentation/DashboardPage.xaml` and is explicitly labeled “no-op for now”.
- There is no binding yet for input text or submit action.

### Dashboard architecture patterns we should preserve
- UI cards are produced via providers (`IDashboardCardProvider` / `IStagedDashboardCardProvider`).
- GitHub access is behind `JitHub.GitHub.Abstractions` and Octokit is isolated in `JitHub.GitHub.Octokit`.
- There is already a cached search service pattern for issues:
  - `IGitHubIssueSearchService` (abstraction)
  - `CachedGitHubIssueSearchService` (cache-first)
  - `OctokitGitHubDataSource.SearchIssuesAsync` (Octokit mapping)
  - `GitHubCacheKeys.SearchIssues(...)` (stable caching)

### AI Dev Gallery reference implementation is included
The reference implementation lives under `JitHub_old/ai-dev-gallery`.

Key patterns we will mirror (conceptually, not verbatim):
- A **model catalog** with “available vs downloaded” states.
- A **download queue** with progress + cancellation and persistence.
- A **model picker** that allows choosing between local runtimes and API-backed runtimes.

Concrete examples in that reference app:
- Download orchestration: `AIDevGallery/Utils/ModelDownloadQueue.cs`, `AIDevGallery/Utils/ModelDownload.cs`
- Persistence of downloaded model inventory: `AIDevGallery/Utils/ModelCacheStore.cs`
- Model selection UI: `AIDevGallery/Controls/ModelPicker/*`
- Provider/config metadata: `AIDevGallery/Models/GenAIConfig.cs`

---

## GitHub Search: what we must support

The ComposeBox search should accept:

1) **Formatted GitHub query syntax**
- Example idea (not normative): qualifiers such as `repo:`, `org:`, `is:issue`, `is:pr`, `author:`, `assignee:`, `label:`, `state:open`, etc.
- These should be forwarded “as-is” (with minimal validation) to the appropriate GitHub search endpoint.

2) **Plain text / natural language**
- Example intent: “open PRs needing review in repo X”
- Convert to a structured query string using a small AI model and then execute the same GitHub search.

### Search domains
We intend to support “all” search domains that GitHub exposes and/or Octokit supports. The primary ones:

- Issues / Pull Requests
- Repositories
- Code
- Users

Reference docs (kept as external links so they stay current):
- GitHub REST Search API: https://docs.github.com/en/rest/search
- Octokit.NET Search client (API surface): https://octokitnet.readthedocs.io/en/latest/ or https://github.com/octokit/octokit.net

> Note: Some GitHub search endpoints have preview headers and/or special constraints. The provider layer should isolate those differences.

### Multi-domain searches (parallel)
The orchestrator must support running **multiple search domains in parallel**.

- The AI query builder may map a natural-language query to **one domain** (e.g., issues) or **multiple** (e.g., issues + repos).
- Even when AI is disabled, we should allow the user to enter a query that runs in multiple domains (e.g., a “search everywhere” mode).
- Results should be returned **grouped by domain** so the UI can render sections cleanly.

---

## Proposed architecture (layers)

At a high level:

```
ComposeBox (UI)
   ↓
ComposeBox ViewModel (input state + submit)
   ↓
Search Orchestrator
   ├─ Query Interpretation (AI optional)
   ├─ Search Provider Selection (domain routing)
   └─ Result Presentation (cards / navigation)

AI Middle Layer
   ├─ Model Catalog (what can run here?)
   ├─ Model Acquisition (download/install guidance)
   ├─ Runtime Providers (local/remote)
   └─ Prompt/Schema (small task-specific)

GitHub Search Layer
   ├─ Abstractions (JitHub.GitHub.Abstractions)
   ├─ Cached services (prefer cache then refresh)
   └─ Octokit implementations (JitHub.GitHub.Octokit)
```

### 1) UI layer
**Goal:** Keep `DashboardPage.xaml` purely UI, with bindings.

Minimal binding targets:
- `ComposeText` (string)
- `SubmitComposeCommand`
- `IsSubmitting` / error message

We should follow existing patterns in the app:
- Use `DashboardViewModel` as the owner of dashboard state (cards + repo context).
- Keep work cancelable; dashboard already cancels work via `Deactivate()`.

### 2) Search orchestrator
A new component responsible for turning user input into a concrete GitHub search request.

Responsibilities:
- Detect whether input is likely “structured” already.
- If natural language: call the AI query builder.
- Select one or more search providers (issues, repos, code, users).
- Execute with the app’s refresh policy (cache-first then background refresh).
- Execute providers **in parallel** where safe.
- Return results grouped by domain in a presentation-friendly model.

Suggested core interface:

- `IComposeSearchOrchestrator`
  - `Task<ComposeSearchResponse> SearchAsync(ComposeSearchRequest request, RefreshMode refresh, CancellationToken ct)`

Key requirement: “multiple ways to search”
- Orchestrator may run **multiple providers** and aggregate results (e.g., issues + repos).
- Orchestrator must be deterministic when AI is disabled/unavailable.

Suggested response shape (conceptual):
- `ComposeSearchResponse`
  - `IReadOnlyList<ComposeSearchResultGroup> Groups`
- `ComposeSearchResultGroup`
  - `Domain` (issues/repos/code/users)
  - `Items` (domain-specific result summaries)

### 3) GitHub search providers
We will model these similarly to the existing issue search service.

Option A (recommended): domain-specific services
- `IGitHubIssueSearchService` (already exists)
- `IGitHubRepoSearchService`
- `IGitHubCodeSearchService`
- `IGitHubUserSearchService`

Option B: one unified `IGitHubSearchService` with a discriminated request type
- Pros: one entrypoint
- Cons: tends to accumulate branching logic and makes caching/mapping messy

**Recommendation:** Use domain-specific services and keep a small aggregator at the orchestrator layer.

Common service-layer rules to keep:
- Abstractions in `JitHub.GitHub.Abstractions`
- Cache-first wrappers in `JitHub.GitHub.Octokit.Services`
- Octokit API calls inside `IGitHubDataSource` implementations
- Stable cache keys in `GitHubCacheKeys`

### 4) AI middle layer (model runtimes + configuration)
The AI layer’s job is narrow:

- Input: natural language and context (selected repo, user identity, possibly recent queries)
- Output: a structured GitHub query string plus optional metadata (confidence, selected domain)

#### Why we need a “mature middle layer”
Because platform capabilities vary, we need:
- A consistent API for the app
- Multiple runtime implementations
- A capability system + user selection

#### Mirror the AI Dev Gallery structure (concepts)
We should mirror these concepts from the reference app:

- **Catalog**: list “available” runtimes/models (including API-based models)
- **Acquisition**: download/install flow where applicable
- **Selection**: user chooses provider/model
- **Persistence**: store selection + downloaded inventory

Suggested abstractions:

- `IAiRuntimeCatalog`
  - Lists available “runtimes” (e.g., Local Foundry, Built-in on device, OpenAI via API key)
- `IAiRuntime`
  - Executes a constrained function: `BuildGitHubSearchQueryAsync(...)`
- `IAiModelStore`
  - Persists chosen runtime/model and any local download state
- `IAiSecretStore`
  - Stores API keys (reuse `ISecretStore` pattern already present in JitHubV3)

#### Targeted providers (initial)
We will explicitly support these providers in the “API-key first” stage:

- OpenAI
- Anthropic
- Azure AI Foundry

The goal is not to hardcode our app to these forever; it’s to ensure the abstraction supports:
- multiple vendors
- multiple model IDs
- provider-specific settings
- a consistent, constrained output schema

#### Platform availability matrix (initial expectations)
This is a planning assumption and should be validated per target:

- Windows:
  - Local runtimes (GPU/CPU), “built-in” models on supported hardware, local Foundry, API-key providers
- macOS:
  - Local Foundry (if installed), API-key providers
- Android/iOS:
  - Initially: API-key providers
  - Later: optional local runtimes if we identify an idiomatic .NET/Uno-friendly approach

The runtime catalog should advertise:
- `IsAvailable` (can it run on this device?)
- `RequiresDownload` / `RequiresInstall`
- `SupportsOffline`

#### Output schema (important)
To keep the model small and robust, the AI should output a constrained schema, e.g.:

- `query`: string (GitHub query syntax)
- `domain`: enum (`issues`, `repos`, `code`, `users`, `auto`)
- `explanation`: short string (optional)

We should treat the AI output as **untrusted**:
- Validate `query` length
- Strip unsupported characters if needed
- Clamp domains

---

## UX and configuration surfaces (high level)

The user requested both API and UI configuration.

- API configuration:
  - Allow programmatic configuration of the default AI runtime/provider.
  - Allow toggling AI on/off.

- UI configuration:
  - Allow choosing the AI provider/model.
  - Allow downloading models (where supported).
  - For local Foundry, provide guidance to install/setup (mirroring the reference app’s approach).

> We’ll decide the exact UI location later (e.g., a settings entry, a dashboard card, or an in-place picker).

---

## Caching and responsiveness

We should preserve existing refresh behavior:

- First render should be fast:
  - `RefreshMode.CacheOnly` path when possible
- Background refresh:
  - `PreferCacheThenRefresh`

Search-specific caching rules:
- Cache key must include:
  - domain (issues/repos/code/users)
  - query string (trimmed)
  - sorting/paging
- Search results should be treated as “quickly stale”; keep TTL short if we later introduce TTL.

---

## Security + privacy considerations

- Never log tokens or API keys.
- Store API keys via the app’s secure storage abstraction (`ISecretStore`).
- Prefer using Uno.Extensions.Storage-backed secure storage where available. Uno.Extensions.Storage supports cross-platform storage (including WebAssembly, Android, iOS, macOS, Desktop, Windows) and uses OS Keychain on Apple platforms (with required entitlements).
- Minimize data sent to remote AI providers:
  - For query building, send the user’s text and minimal context.
  - Do not send repository content by default.

---

## Staged implementation roadmap

### Phase 0 — Non-AI search (foundation)
- Bind ComposeBox input and “Send” to a `DashboardViewModel` command.
- Implement a minimal orchestrator:
  - if user enters a query string → execute issue search (`IGitHubIssueSearchService`) and show results.
- Result presentation (decided):
  - reuse dashboard cards, but introduce a clean abstraction so “search results cards” don’t leak provider-specific UI into the orchestrator.
  - render results as grouped sections (by domain) using the same card host infrastructure.

### Phase 1 — AI query builder via API key (cross-platform baseline)
- Add an `IAiRuntime` implementation backed by an API-key provider.
- Add `ISecretStore` wiring for the key.
- Keep output schema constrained and always show/allow editing the final query string.

### Phase 2 — Windows-first local runtimes (mirror AI Dev Gallery)
- Introduce model catalog + downloader and selection UI.
- Start by mirroring concepts in the reference app:
  - download queue, persistent cache store, model picker.

### Phase 3 — Local Foundry guided setup
- Add runtime provider that detects Foundry installation.
- Add guidance UI for install/setup, mirroring the reference pattern.

### Phase 4 — Fine-tuning (late stage)
We will likely need a very small, task-specific model for “natural language → GitHub query syntax”.

Design goals:
- extremely constrained output schema (query + domain(s) + optional explanation)
- low latency
- robust against prompt injection and irrelevant instructions
- does not require repo content by default

Practical plan:
- Start with a supervised dataset of `(input_text, expected_query, expected_domains)`.
- Add “hard cases” covering common GitHub search qualifiers.
- Evaluate with deterministic metrics:
  - exact match (or normalized match) on query
  - domain classification accuracy
  - safety checks (no secrets leakage; bounded output)

Runtime integration:
- Ship as an optional `IAiRuntime` implementation.
- Run locally when supported (GPU/CPU) and fall back to API providers when not.

### Phase 5 — Platform-specific runtime implementations (late stage)
Not all AI runtimes will be available on all platforms. We will treat platform-specific implementations as additive, late-stage work, while keeping the interfaces stable.

Targets to explore:
- Windows: local GPU/CPU runtimes + built-in models on supported hardware + local Foundry.
- macOS: local Foundry (where available) and compatible local runtimes.
- iOS/Android: start with API-based providers; later explore local runtimes that can be hosted idiomatically in .NET/Uno.
- Linux: explore running ONNX-based models on CPU and (where available) GPU. The goal is an `IAiRuntime` implementation that can run a small model locally without requiring cloud connectivity.

---

## Notes on decisions (baked in)

- Results UX: reuse dashboard cards (with a clean card factory/adapter layer).
- Multi-domain search: run providers in parallel and return results grouped by domain.
- API-key providers: OpenAI, Anthropic, Azure AI Foundry.
- Platform-specific runtime work: explicitly scheduled late, but interfaces designed upfront.

