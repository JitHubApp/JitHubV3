# JitHub GitHub Service Layer + Data/Networking Architecture

This document proposes an architecture for JitHub’s **GitHub service layer** and **networking/data layer**.

It is designed so the UI can grow toward “native GitHub.com parity” (repos, issues, PRs, discussions, projects, activities, settings), while keeping the app code insulated from changes in GitHub API access strategies (Octokit.NET vs REST vs GraphQL vs generated SDKs).

This doc is intentionally optimized for:

- **Performance-first UX:** cached results show instantly, then refresh in the background.
- **Isolation:** UI depends on *our* interfaces and models, not Octokit types.
- **Correctness:** cache invalidation and polling are explicit and cancellable.
- **Testability:** most logic is unit-testable without network.

---

## Scope and POC

**POC pages (initial scope):**

1) Main page: list current user’s repositories.
2) Repo issues page: list issues for a selected repo (treat as “conversations” list).

**Behavioral requirements:**

- Cache every network call.
- On navigation, **serve cached first** (if available), then **refresh** (revalidate) to get fresh data.
- On “conversation-like” pages (issues/PRs/discussions), add **polling** with a configurable interval (default **10 seconds**) to refresh.
- Networking/data work must not harm UI responsiveness.

---

## Existing repo context (what we build on)

### OAuth is already server-mediated

The repository already implements a server-mediated GitHub OAuth flow in the server project:

- The server exchanges the OAuth code for a token.
- The client obtains token information via an exchange endpoint.

The service layer on the client should treat auth as a **token provider dependency**, not an implementation detail.

### A prior cache prototype exists (JitHub_old)

The older code contains an `InMemoryCache` concept that:

- Subscribes to a URL key
- Can poll with timers
- Calls an invalidation callback

We will keep the high-level idea (subscription + invalidation), but implement it in a more robust, testable, and cancellable way.

---

## High-level architecture

### Layering

The app should depend on GitHub through a thin, stable set of interfaces:

- **UI layer (Uno / MVUX / MVVM)**
  - ViewModels request data via feeds/observables and render it.
  - Navigation determines which data to load.

- **Application layer (use-case orchestration)**
  - Coordinates pages’ flows like “load repos” / “load issues”.
  - Owns refresh policy for pages (stale-while-revalidate + optional polling).

- **Domain models (JitHub models)**
  - DTOs designed for JitHub UI needs.
  - No Octokit types exposed.

- **GitHub service façade (our abstraction)**
  - E.g. `IGitHubRepositoryService`, `IGitHubIssueService`, etc.
  - Interfaces define *what* we need, not *how* to fetch it.

- **Provider adapters (implementation)**
  - Initially: `OctokitProvider` (Octokit.NET REST SDK).
  - Future: `RestProvider` (raw HTTP), `GraphQLProvider`, or generated clients.

- **Networking + cache runtime**
  - A shared `HttpClient` setup (naming, headers, resilience).
  - Cache store, invalidation, polling, and de-duplication.

### Proposed projects (library split)

Keep the GitHub client stack in separate library projects:

- `JitHub.GitHub.Abstractions`
  - Interfaces and JitHub models.
- `JitHub.GitHub.Octokit`
  - Octokit-based implementation.
- `JitHub.Data.Caching`
  - Cache primitives, invalidation streams, polling.
- `JitHub.Networking`
  - Http pipeline configuration and resilience.

The UI project should reference only `JitHub.GitHub.Abstractions` (and the composition root references the implementation projects).

---

## Uno.Extensions grounding (how this fits Uno best practices)

This architecture aligns with Uno.Extensions guidance:

- **DI via hosting:** register services using `IHostBuilder.ConfigureServices(...)` and consume via constructor injection.
- **Configuration:** load settings from embedded `appsettings.json` using `UseConfiguration()` and `EmbeddedSource<App>()`.
- **HTTP:** centralize endpoints/named clients through configuration so all services share consistent `HttpClient` behavior.
- **MVUX feeds caching:** MVUX feed subscriptions have a concept of subscription caching (replay last value) within a viewmodel context.

Even if we don’t use MVUX everywhere immediately, we should design our service layer so it can expose **reactive data sources** cleanly.

---

## Data access primitives

### 1) Auth abstraction

The GitHub provider needs tokens, but the UI shouldn’t care how they’re acquired.

Define:

- `IGitHubAuthSession`
  - Has `AccessToken` (or provides a safe `GetTokenAsync`).
  - Notifies when token changes (logout/login).

- `IGitHubTokenProvider`
  - `Task<string?> GetAccessTokenAsync(CancellationToken ct)`
  - `IObservable<TokenChanged>` or an event for invalidation.

**Token persistence is not optional.** Access tokens must be stored in a **secure, on-disk** location using the platform’s recommended secret store. Do not store tokens in the cache layer.

#### Secure token storage (Uno guidance + platform best practice)

Uno Platform documents cross-platform support for `Windows.Security.Credentials.PasswordVault`, intended for storing credentials and tokens. On supported platforms it uses hardware-backed encryption mechanisms:

- **Windows:** stored via the platform credentials manager.
- **Android:** backed by **AndroidKeyStore** (key material stays in the keystore; data is encrypted and persisted in the app directory).
- **iOS:** stored in the **Keychain** (recommended iOS secret store).

Important caveats from Uno’s documentation:

- **Skia desktop (Linux / older Windows via Skia):** Uno does not support `PasswordVault` there.

Therefore, the app should implement a small abstraction such as `ISecretStore` (get/set/delete) with platform-specific implementations:

- Prefer `PasswordVault` on Windows/Android/iOS where available.
- For Apple platforms when using Uno.Extensions that rely on Keychain (e.g., Uno.Extensions.Storage used by authentication scenarios), ensure the required Keychain entitlements are configured.
- For Skia desktop targets: use the OS-native secret store (e.g., Keychain on macOS, Secret Service/libsecret on Linux) when we add support; until then, treat token persistence as a platform capability that must be implemented before enabling GitHub auth on that target.

### 2) Request identity (cache key)

Caching must be stable across providers. Don’t key purely by URL; key by a typed descriptor.

Use a normalized cache key type:

- `CacheKey`
  - `string Operation` (e.g. `"repos.list"`, `"issues.list"`)
  - `ImmutableDictionary<string,string>` parameters (owner/repo/page/state)
  - `string? UserScope` (so multi-account doesn’t cross-contaminate)

This prevents accidental collisions and makes cache introspection/debugging possible.

### 3) Cache entry + freshness

Each cache entry should track:

- `Value` (typed)
- `FetchedAt` (UTC)
- `ExpiresAt` (UTC) — optional TTL
- `ETag` / `Last-Modified` — optional conditional request metadata
- `LastError` — optional (for stale-but-usable)

We want **stale-while-revalidate**:

- Return cached value immediately.
- Trigger a background refresh.
- If refresh yields a different value, publish an update.

### 4) Invalidation channel

UI must be able to observe changes.

This architecture intentionally does **not** standardize on a single reactive primitive. Different parts of the UI may choose different patterns:

- **MVUX** where it fits (feeds/state patterns).
- **MVVM** where needed, using `IObservable<T>` (or events) for update streams.

The service and cache layers should be shaped so they can be adapted into either model without rewriting networking/caching logic.

The key requirement: **subscribers receive the cached value quickly** and then receive updates when refresh/polling completes.

---

## Threading + responsiveness

### Key principle

Avoid doing heavy work on the UI thread.

- Network calls are naturally async; the danger is *continuations and callbacks*.
- The cache layer must not assume a UI context.

### Rules

- Library/service code should use `ConfigureAwait(false)`.
- Cache invalidation notifications should be raised on a background thread by default.
- UI layer is responsible for marshaling to UI thread (or MVUX does this for you).

### Worker execution model

We still want a single, controlled execution pipeline to avoid bursty work:

- A background **request coordinator** that:
  - Deduplicates inflight requests per `CacheKey`.
  - Limits concurrency (e.g., 4–8 concurrent outbound calls).
  - Implements retry/backoff for transient failures.
  - Observes cancellation and page lifetime.

Implementation strategies:

- Channel-based queue (`Channel<WorkItem>` with a single consumer).
- Semaphore-limited concurrency + inflight dictionary.

---

## Polling model (issues/PRs/discussions)

### Requirement

For “conversation-like” pages, refresh periodically (default 10 seconds).

### Design

Polling is a policy attached to a query/subscription:

- Start polling when page becomes active.
- Stop polling when navigating away or app is suspended.
- Use a `CancellationToken` owned by the viewmodel/page scope.

### Avoid thundering herd

- Poll timers should align to a jittered schedule (e.g., 10s ± up to 1s).
- Respect rate limits and back off if GitHub returns abuse/rate-limit headers.

---

## Provider adapters (start with Octokit.NET)

### Why a provider layer

We want to be able to swap:

- Octokit REST
- Raw REST (HttpClient)
- GraphQL (experimental SDK / generated client)

without rewriting UI.

### Octokit adapter boundaries

The Octokit implementation should be isolated behind internal mapping code:

- Octokit models → JitHub models
- Octokit pagination → JitHub paging
- Octokit exceptions → normalized error types

Do **not** leak Octokit types out of the provider assembly.

### Networking concerns

Octokit has its own HTTP pipeline, but we still want:

- A consistent User-Agent
- A consistent auth source (`IGitHubTokenProvider`)
- Standard logging and correlation IDs

If Octokit doesn’t allow full alignment with our `HttpClientFactory` configuration, keep that mismatch contained inside `JitHub.GitHub.Octokit`.

---

## Caching strategy details

### Default policy

- Cache every query.
- Serve cached immediately if present.
- Refresh on page navigation (revalidate).

### Persistence (intentionally memory-only for now)

Early in development, JitHub’s domain models and cache schemas will change frequently. Persisting cache entries to disk would quickly introduce invalid data and migration burden.

For that reason:

- The **data cache is in-memory only** (LRU + TTL/metadata).
- Restarting the app may require refetching data.
- When we later want disk caching, it should be introduced only after models stabilize and we have an explicit versioning/migration story.

### Conditional requests (optional but high value)

If we can store ETags / Last-Modified:

- Send `If-None-Match` / `If-Modified-Since`.
- Treat `304 Not Modified` as a successful refresh.

This reduces bandwidth and helps with rate limits.

### Inflight de-duplication

If multiple consumers request the same `CacheKey`:

- Share one network call.
- Fan-out the results to all subscribers.

### Memory constraints

Define a maximum cache size:

- LRU eviction (by key)
- Optional per-entry size estimation

For the POC, an in-memory LRU is enough.

---

## Error handling and UX expectations

### Error normalization

Introduce a small set of normalized errors:

- Unauthorized (401) → prompt login.
- Forbidden (403) with rate limit → show “rate limited” + retry after.
- Network offline / timeout → show stale data + banner.

### Stale-but-usable

If cached value exists and refresh fails:

- Keep showing cached.
- Emit an update containing the error for UI to show non-blocking status.

---

## Telemetry and logging

- Log per operation + duration + cache hit/miss.
- Log rate limit headers when present.
- Add correlation IDs per request chain (page navigation → queries).

---

## POC flows (repos → issues)

### 1) Repos list

- Query: `repos.list` (current user)
- UI behavior:
  - Show cached list immediately if available.
  - Trigger refresh (revalidate).
  - Update list when refresh completes.

### 2) Issues list for repo

- Query: `issues.list` with parameters:
  - owner, repo
  - filters (open/closed/all) (can default to open)
  - paging (page size, page/cursor)

- UI behavior:
  - Show cached list immediately.
  - Refresh immediately.
  - Start polling every 10 seconds while page is active.
  - For the POC UI, it is acceptable to fetch only the first page. Infinite scroll is a later UI task, but the service APIs must be paging-capable from day one.

---

## Recommended initial interface set (POC)

Define narrow interfaces first; add breadth as we add features.

To keep both MVUX and MVVM viable, expose **request/response** APIs at the abstraction layer, and add MVUX feed / observable adapters above them.

- `IGitHubRepositoryService`
  - `Task<IReadOnlyList<RepositorySummary>> GetMyRepositoriesAsync(RefreshMode refresh, CancellationToken ct)`

- `IGitHubIssueService`
  - `Task<PagedResult<IReadOnlyList<IssueSummary>>> GetIssuesAsync(RepoKey repo, IssueQuery query, PageRequest page, RefreshMode refresh, CancellationToken ct)`

Supporting types:

- `RepoKey` (Owner, Name)
- `RepositorySummary` (Id, Name, OwnerLogin, IsPrivate, DefaultBranch, Description, UpdatedAt)
- `IssueSummary` (Id, Number, Title, State, Author, CommentCount, UpdatedAt)

Paging types (required even if the POC UI doesn’t implement infinite scrolling yet):

- `PageRequest` (page size + either page number or cursor)
- `PagedResult<T>` (items + next page token/cursor + optional total/count hints)

`RefreshMode` should support at least:

- `PreferCacheThenRefresh` (stale-while-revalidate)
- `ForceRefresh`
- `CacheOnly` (useful for offline modes and startup rendering)

Note: Whether to return `IFeed<T>` vs `Task<T>` + invalidation stream is an implementation choice; pick one as a standard early.

---

## Implementation plan (phased)

### Phase A — Abstractions

- Create `JitHub.GitHub.Abstractions`
- Add models + service interfaces + error contracts.

### Phase B — Cache runtime

- Add `JitHub.Data.Caching`
- Implement:
  - cache keying
  - stale-while-revalidate
  - invalidation stream
  - inflight de-dup
  - optional polling policy

### Phase C — Octokit provider

- Add `JitHub.GitHub.Octokit`
- Map Octokit → JitHub models
- Integrate auth token provider

### Phase D — Wire to UI

- Register services using Uno.Extensions Hosting DI.
- Main page uses repos feed; issues page uses issues feed + polling.

---

## Open questions (choose early)

## Decisions baked into this design

- **Reactive approach:** do not enforce one primitive; support MVUX where it fits and MVVM with `IObservable`/events via adapters.
- **Cache persistence:** keep the data cache **in-memory only** until models stabilize and we can plan migrations.
- **Token storage:** persist tokens securely on disk using platform secret stores (prefer Uno `PasswordVault` where supported).
- **Paging:** service APIs must be paging-capable immediately; infinite scroll UI comes after the POC.

## Remaining open questions

1) For Skia desktop targets, which exact native secret-store implementation do we standardize on (and do we gate GitHub auth on it)?
2) Do we adopt ETag/conditional requests for all list endpoints early, or phase it per-feature?
3) What is our initial rate-limit/backoff policy (especially for 10s polling) and how do we surface it in UI?

---

## Appendix: Uno.Extensions excerpts referenced

- DI: services registered via `IHostBuilder.ConfigureServices(...)`.
- Configuration: `UseConfiguration(...)` + `EmbeddedSource<App>()` to load `appsettings.json`.
- MVUX: subscription caching replays last values within a viewmodel context.
