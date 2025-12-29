# JitHub GitHub Service Layer — Execution Plan (Phase-by-Phase)

This plan is derived from [docs/github-service-layer-architecture.md](docs/github-service-layer-architecture.md). It is intentionally detailed and structured into phases and sub-phases, with explicit testing and validation gates.

The goal is to build a **fast, testable, cache-first** GitHub data stack that supports:

- Server-mediated OAuth already present in this repo
- Cached-first, stale-while-revalidate behavior everywhere
- Explicit invalidation and optional polling for “conversation-like” pages
- Background execution so the UI stays responsive
- A proof-of-concept UX: **Repos list → Repo issues list**

---

## Guiding principles (applies to every phase)

1) **UI remains insulated from providers**
- UI code depends on our interfaces and models only.
- Octokit (or any future provider) is behind an adapter boundary.

2) **Cache-first always**
- Every query must have a cache key.
- Cached data should render immediately when available.
- Refreshes happen in the background and publish updates.

3) **No forced reactive primitive**
- We intentionally support both MVUX-style patterns and MVVM via `IObservable`/events.
- Core services should remain consumable from either approach.

4) **Memory-only cache (for now)**
- Cache persistence is explicitly out-of-scope until models stabilize.

5) **Cancellation is non-negotiable**
- Navigation-driven work must cancel when the page deactivates.
- Polling must stop when leaving the page.

6) **Tests are the gate**
- The cache/invalidation engine is test-first.
- Provider adapters have unit tests using fakes/stubs, and a thin integration-test layer if needed.

---

## Definition of Done (global)

A phase is “done” when:

- Unit tests are added and green for the phase’s core behaviors.
- Any new public APIs have docs and examples.
- The app demonstrates the feature (where relevant) without UI stalls.
- Cancellation paths are covered by tests (no orphan polling).

---

## Phase 0 — Baseline inventory, configuration, and scaffolding

### 0.1 Align with existing OAuth/server setup

Deliverables:

- Confirm how the client receives an access token today (from the server exchange endpoint) and where that token should be exposed (client-side token provider).
- Document the required `appsettings.*.json` knobs for:
  - GitHub API base URL (usually `https://api.github.com/`)
  - OAuth server base URL (already exists)
  - Polling defaults (10s)

Validation:

- Confirm a minimal “I have a token” path exists in client app execution for at least one target head.

### 0.2 Create (or confirm) project layout for the GitHub stack

Deliverables:

- Add new projects (or create a minimal skeleton in the existing structure if preferred):
  - `JitHub.GitHub.Abstractions`
  - `JitHub.Data.Caching`
  - `JitHub.GitHub.Octokit`
  - Optional: `JitHub.GitHub.Tests` (unit tests for abstractions + caching + adapter mapping)
- Add to the solution structure.

Unit tests:

- A minimal “smoke compile + basic contract tests” project.

---

## Phase 1 — Abstractions (interfaces + JitHub models)

### 1.1 Define identity and model primitives

Deliverables:

- `RepoKey` (Owner, Name)
- `UserKey` or account scope identifier (for cache isolation)
- `RepositorySummary` and `IssueSummary` (POC-focused)
- `IssueQuery` (state/open/closed/all; sort; optional labels later)

Unit tests:

- Determinism tests for any normalization helpers (e.g., repo key casing normalization if implemented).

### 1.2 Define refresh and paging primitives

Deliverables:

- `RefreshMode`
  - `PreferCacheThenRefresh`
  - `ForceRefresh`
  - `CacheOnly`
- `PageRequest`
  - Supports a page size and *either* (a) page number or (b) cursor token (implementation-defined)
- `PagedResult<T>`
  - `Items`
  - `NextPage` (cursor/page) if available

Unit tests:

- Serialization/format tests for paging tokens if we decide to store them in cache keys.

### 1.3 Define service interfaces (POC-focused)

Deliverables:

- `IGitHubRepositoryService`
  - `Task<IReadOnlyList<RepositorySummary>> GetMyRepositoriesAsync(RefreshMode refresh, CancellationToken ct)`
- `IGitHubIssueService`
  - `Task<PagedResult<IReadOnlyList<IssueSummary>>> GetIssuesAsync(RepoKey repo, IssueQuery query, PageRequest page, RefreshMode refresh, CancellationToken ct)`

Design note:

- Keep interfaces “request/response” so they can be adapted into MVUX feeds or MVVM observables.

Unit tests:

- Contract-focused tests (argument validation policies, null-handling rules).

---

## Phase 2 — Cache runtime (in-memory SWR + invalidation)

This is the heart of the architecture. Build it as a standalone unit-testable library.

### 2.1 Cache key model

Deliverables:

- `CacheKey` with:
  - operation name
  - normalized parameter dictionary
  - user/account scope
- A stable string representation for logging and debugging.

Unit tests:

- Equality and hashing (no collisions for common permutations).
- Parameter normalization (ordering invariants).

### 2.2 In-memory cache store

Deliverables:

- `ICacheStore` abstraction with an in-memory implementation:
  - get/set
  - LRU eviction
  - optional TTL metadata

Unit tests:

- LRU eviction correctness.
- TTL expiry behavior.

### 2.3 Stale-while-revalidate execution helper

Deliverables:

- A helper (e.g., `CacheRuntime`) that supports:
  - return cached value immediately
  - optionally trigger a refresh
  - publish updates when refresh completes
  - inflight de-duplication per `CacheKey`

Unit tests (core gate):

- Cache hit returns immediately.
- Refresh is started exactly once for concurrent callers.
- Refresh updates are published.
- Refresh failure keeps cached value and publishes an error update.

### 2.4 Update publication mechanism (reactive-neutral)

Deliverables:

- A non-UI, reactive-neutral update channel, such as:
  - an event-based `ICacheInvalidationBus`, or
  - an `IObservable<CacheChanged>` facade layered on top of the bus.

Unit tests:

- Subscribers receive updates in order.
- No updates after cancellation/disposal.

### 2.5 Polling policy support

Deliverables:

- Polling scheduler that:
  - attaches to a query scope
  - runs refresh periodically (default 10s)
  - supports jitter
  - cancels cleanly

Unit tests:

- Poll triggers refresh at expected cadence.
- Poll stops immediately on cancellation.
- Jitter stays within bounds.

---

## Phase 3 — Provider adapter (Octokit) and mapping layer

### 3.1 Token provider and secret store integration

Deliverables:

- `IGitHubTokenProvider` implementation wired to the app’s auth flow.
- A secure secret-store abstraction and implementation strategy:
  - Prefer `Windows.Security.Credentials.PasswordVault` where supported (Windows/Android/iOS).
  - Ensure Apple entitlements for Keychain scenarios are addressed where applicable.

Unit tests:

- Token provider behavior (token present/absent, refresh of token event).
- Secret-store fakes for unit tests.

### 3.2 Octokit client configuration

Deliverables:

- Octokit client setup with:
  - User-Agent
  - token auth
  - base address
  - logging hooks

Unit tests:

- Verify Octokit calls are constructed with expected parameters (via adapter fakes or wrapper seams).

### 3.3 Mapping: Octokit → JitHub models

Deliverables:

- Pure mapping functions:
  - repository mapping
  - issue mapping

Unit tests (core gate):

- Mapping correctness for key fields.
- Null/missing fields behavior.

---

## Phase 4 — Compose services: GitHub services backed by cache runtime

### 4.1 Implement `IGitHubRepositoryService` with caching

Deliverables:

- Cache key definition for `repos.list`.
- SWR behavior:
  - cached first
  - refresh in background

Unit tests:

- Cache key correctness.
- Cache-first behavior.

### 4.2 Implement `IGitHubIssueService` with caching + polling

Deliverables:

- Cache key definition for `issues.list` (include repo, query filters, page request).
- Polling support on issue list pages (but service itself remains UI-agnostic).

Unit tests:

- Poll-driven refresh uses same inflight de-dup path.
- Cache key includes paging inputs.

---

## Phase 5 — UI POC (Repos → Issues), with “good enough” paging

This phase is about validating the stack end-to-end, not perfect UX.

### 5.1 Repos list page wiring

Deliverables:

- Main page loads repos and renders quickly.
- Uses cached-first flow where possible.

Validation:

- Navigate away during loading: no crash, no UI freeze.

### 5.2 Issues list page wiring

Deliverables:

- Issues page loads first page only (acceptable for POC).
- Starts polling while active and stops when navigating away.

Validation:

- Observe updates arriving without flicker.
- Confirm polling stops immediately on back navigation.

---

## Phase 6 — Hardening: resilience, rate limits, and correctness

### 6.1 Rate-limit and abuse handling

Deliverables:

- Normalized error mapping for:
  - 401 unauthorized
  - rate-limited scenarios
  - transient network errors
- Backoff policy for polling when rate-limited.

Unit tests:

- Backoff behavior triggers and recovers.

### 6.2 Diagnostics and observability

Deliverables:

- Structured logs for:
  - cache hit/miss
  - refresh duration
  - polling tick
  - inflight de-dup

Unit tests:

- Minimal “logs emitted” test if logging is abstracted.

---

## Phase 7 — Paging expansion + infinite scroll UI (post-POC)

This phase is explicitly after the POC.

### 7.1 Service-level paging validation

Deliverables:

- Add a small paging test harness to confirm:
  - requesting next pages works
  - cache keys don’t collide across pages

### 7.2 Infinite scroll UI implementation

Deliverables:

- Decide UI implementation per page:
  - MVUX feed-based infinite list, or
  - MVVM + `IObservable` incremental loading
- Implement incremental loading triggers and UI virtualization.

Validation:

- No UI stalls.
- Smooth loading while scrolling.

---

## Phase 8 — Feature expansion (after stack proves itself)

Possible next services:

- Pull requests (list + details)
- Discussions (list + details)
- Notifications
- Search

Each added surface follows the same pattern:

- Abstraction interface + models
- Provider mapping
- Cache key + SWR + (optional) polling
- UI wiring
- Tests
