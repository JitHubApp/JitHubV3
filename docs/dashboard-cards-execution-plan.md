# JitHubV3 Dashboard Cards — Execution Plan + Octokit API Map (Phase 9+)

This plan extends:

- [docs/dashboard-ui-architecture.md](docs/dashboard-ui-architecture.md)
- [docs/dashboard-ui-execution-plan.md](docs/dashboard-ui-execution-plan.md) (Phases 0–8 delivered the host/layout/animations/tests)
- [docs/github-service-layer-architecture.md](docs/github-service-layer-architecture.md)

**Goal:** implement “real” dashboard cards (providers backed by GitHub data) while keeping the dashboard host stable.

---

## Ground rules (non-negotiable)

1) **UI never depends on Octokit types**
- Dashboard providers depend on `JitHub.GitHub.Abstractions` services/models.
- Octokit remains behind `JitHub.GitHub.Octokit` via `IGitHubDataSource` + cached services.

2) **Cache-first always**
- Providers should request `RefreshMode.CacheOnly` first (fast UI), then `PreferCacheThenRefresh`.
- Providers must tolerate “no cached value yet” (empty cards are OK; don’t block the UI thread).

3) **Cancellation and deactivation**
- Providers must honor `CancellationToken` and stop work when the dashboard deactivates.

4) **No new UX surfaces**
- This plan only adds cards (content) inside the existing dashboard card host.
- Do not add new pages/modals/filters/toggles beyond what card actions already support.

---

## Current capability inventory (what we can build on today)

### Existing GitHub abstractions

Already present in `JitHub.GitHub.Abstractions` + wired in DI:

- `IGitHubRepositoryService` (my repositories)
- `IGitHubIssueService` (repo issues list, optional search text)
- `IGitHubIssueConversationService` (issue detail + comments)
- `IGitHubIssuePollingService` (poll issues list)

### Existing Octokit provider boundaries

In `JitHub.GitHub.Octokit`:

- `OctokitGitHubDataSource` currently uses:
  - repositories: `client.Repository.GetAllForCurrent(...)`
  - issues list: `client.Issue.GetAllForRepository(...)`
  - issues search: `client.Search.SearchIssues(...)`
  - issue detail: `client.Issue.Get(...)`
  - issue comments: `client.Issue.Comment.GetAllForIssue(...)`

This is a strong foundation for an initial wave of meaningful cards without introducing new API families yet.

---

## Card/provider model constraints

This plan assumes the existing app-layer primitives:

- `IDashboardCardProvider` (pluggable providers)
- `DashboardContext` (current account scope + `SelectedRepo`)
- `DashboardCardModel` (title/subtitle/summary/actions/importance)

Important design note:
- Prefer **a few “summary cards”** for dense domains like issues/search, but **treat notifications and activity as feed-style cards** (one card per notification/event). This makes the dashboard feel alive without adding new UX surfaces.

---

## Performance: staged loading + bounded parallelism (chronological UX)

Some cards can be produced from **one API call that yields multiple items** (high-yield), while others require **many calls** (low-yield or enrichment). The dashboard should feel fast by intentionally staging work.

### Provider tiers (how we schedule work)

Treat providers as belonging to one of these scheduling tiers:

- **Tier 0 (No network):** pure context/local (e.g., “Selected repo”).
- **Tier 1 (Single-call, multi-card):** one request can generate multiple cards (or many rows summarized into few cards).
  - Example: “Pinned/recent repos” derived from `IGitHubRepositoryService` results.
  - Example: “My assigned issues” via a single Search query.
  - Example: “Notifications (unread)” where each notification is its own card.
  - Example: “Recent activity” where each event is its own card.
- **Tier 2 (Single-call, single-card):** one request yields one card (still fine, but not as dense).
- **Tier 3 (Multi-call/enrichment):** requires multiple API calls (per-repo fan-out, per-item details, comments, checks, etc.).

Rule of thumb:
- Prefer Tier 1 providers on initial dashboard load.
- Tier 3 providers must run **only after** the user has a useful dashboard on screen.

### Execution strategy (what the dashboard host should do)

1) **Fast path: render Tier 0 + Tier 1 using cache-only first**
- On activation (and when `SelectedRepo` changes), request `RefreshMode.CacheOnly`.
- Apply results incrementally per-provider (in-place sync by card id) so the UI does not “flash”.

2) **Background path: refresh + slower providers**
- Start a second wave using `RefreshMode.PreferCacheThenRefresh` in the background.
- Tier 3 providers must be cancellable and should stop when the dashboard deactivates or context changes.

3) **Bound parallelism**
- Run provider fetches concurrently, but with a small limit (e.g., 3–6) to avoid saturating bandwidth/CPU and to reduce rate-limit pressure.
- Avoid N+1 patterns where possible; if unavoidable, cap concurrency inside the provider too.

4) **Stable ordering with incremental updates**
- Maintain a stable deterministic ordering across waves (e.g., by `provider.Priority`, then `card.Importance` desc, then `card.CardId`).
- When a provider updates, update only its card subset and keep unrelated cards stable.

5) **Reuse warmed cache entries**
- Prefer designing providers to reuse cache already warmed by the sidebar and pages (e.g., repo list cache entry).
- A provider that needs the same data as the repo list should not trigger duplicate network calls; it should use cache-first reads.

### “No glitch” UI rule

- Cards should appear progressively (fast cards first), without clearing the full list.
- Never block the UI thread for card fetching.
- Avoid full-collection resets; sync by identity.

---

## Card catalog (ideas + Octokit API mapping)

Each entry below is written as:

- **User value** — why this card belongs on the dashboard
- **Scope** — global vs selected repo
- **Abstractions needed** — whether we already have an `IGitHub*Service`
- **Octokit API area** — what the provider would call under the hood
- **Refresh policy** — cache-first vs polling
- **Actions** — what swipes/buttons should do

### A) “Selected repo” and repo-centric cards (low-risk, uses existing repo + issues services)

1) **Selected repository (existing)**
- User value: confirms context.
- Scope: selected repo.
- Abstractions needed: none.

2) **Open issues summary**
- User value: quick health snapshot of the selected repo.
- Scope: selected repo.
- Abstractions needed: reuse `IGitHubIssueService`.
- Octokit API area: issues list (`client.Issue.GetAllForRepository`) or issues search (`client.Search.SearchIssues`).
- Refresh policy: cache-first; optional polling when repo is selected.
- Actions:
  - Primary: Navigate to Issues page (Open).
  - Secondary: Switch query (Closed/All) (optional, but avoid new UI; could be separate cards).

3) **Recently updated issues**
- User value: “what changed since I last looked”.
- Scope: selected repo.
- Abstractions needed: reuse `IGitHubIssueService`.
- Octokit API area: issues list with sort (if exposed by abstraction) or search query (e.g., `updated:>...`).
- Refresh policy: cache-first; polling can be enabled (10–30s) when this card is visible.
- Actions:
  - Open issue conversation.
  - Dismiss (already supported).

4) **Stale issues needing attention**
- User value: surfaces inactivity hotspots.
- Scope: selected repo.
- Abstractions needed: likely extend `IssueQuery` (e.g., “updated before X”) or add a specialized query.
- Octokit API area: issues search (search supports richer filters than list).
- Refresh policy: cache-first, no polling.

### B) “My work” cards (high value, likely needs new services)

5) **Assigned issues (across all repos)**
- User value: the classic “what I should do next” card.
- Scope: global.
- Abstractions needed: new `IGitHubMyWorkService` or `IGitHubIssueSearchService`.
- Octokit API area: issues search (`client.Search.SearchIssues`) with query like `is:issue is:open assignee:@me` (plus optional org/repo filters).
- Refresh policy: cache-first; polling optional.
- Actions:
  - Open issue conversation.
  - “Snooze”/Dismiss (maps to local-only dismissal; no server mutation).

6) **Mentioned / participating issues**
- User value: “threads I’m involved in”.
- Scope: global.
- Abstractions needed: new `IGitHubIssueSearchService`.
- Octokit API area: issues search (query like `mentions:@me` or `involves:@me`).
- Refresh policy: cache-first.

7) **PRs requiring my review**
- User value: high-signal PR workflow.
- Scope: global.
- Abstractions needed: new `IGitHubPullRequestService` (or `IGitHubPullRequestSearchService`).
- Octokit API area:
  - Prefer search via `client.Search.SearchIssues` using `is:pr review-requested:@me is:open`.
  - Alternative: PR endpoints (`client.PullRequest.*`) for repo-scoped lists.
- Refresh policy: cache-first; polling optional.

### C) Notifications + inbox (very dashboard-native)

8) **Notifications (unread)**
- User value: GitHub inbox in one glance.
- Scope: global.
- Abstractions needed: new `IGitHubNotificationService`.
- Octokit API area: notifications (`client.Activity.Notifications.*` in Octokit.NET).
- Refresh policy: cache-first; polling recommended (10–30s) with jitter and backoff.
- Card shape: **one card per notification** (title = notification title, subtitle = repo, summary = type + timestamp).
- Actions:
  - Open target (issue/PR) when resolvable.
  - Mark as read (server mutation) (only if we’re ready to add mutation support).

### D) Activity and recency (good “feed-like” cards)

9) **Recent activity (my user events)**
- User value: quick recap of recent work.
- Scope: global.
- Abstractions needed: new `IGitHubActivityService`.
- Octokit API area: events/activity (`client.Activity.Events.*`).
- Refresh policy: cache-first; polling optional.
- Card shape: **one card per event** (title/subtitle/summary derived from repo/type/actor/time).

10) **Recent activity (selected repo events)**
- User value: “what’s happening in this repo”.
- Scope: selected repo.
- Abstractions needed: `IGitHubActivityService`.
- Octokit API area: repo events (`client.Activity.Events.*` repo endpoints).
- Refresh policy: cache-first.
- Card shape: **one card per event**.

### E) Repo overview cards (depends on enriching repository model)

11) **Repo snapshot (stars/forks/watchers, last updated, default branch)**
- User value: one-card repo overview.
- Scope: selected repo.
- Abstractions needed:
  - Either extend `RepositorySummary`, or add `IGitHubRepositoryDetailsService`.
- Octokit API area: repository details (`client.Repository.Get(...)`).
- Refresh policy: cache-first.

12) **Pinned / recently updated repositories**
- User value: quick “jump back in” list.
- Scope: global.
- Abstractions needed: reuse `IGitHubRepositoryService` + local ranking (recent updates).
- Octokit API area: already available via `GetAllForCurrent`.
- Refresh policy: cache-first.

### F) Longer-term / optional cards (be cautious: Octokit coverage may vary)

13) **Releases (latest)**
- User value: “what shipped”.
- Scope: selected repo.
- Abstractions needed: new `IGitHubReleaseService`.
- Octokit API area: releases (`client.Repository.Release.*` in Octokit.NET).

14) **CI / workflow health**
- User value: “is main green?” at a glance.
- Scope: selected repo.
- Abstractions needed: likely a dedicated service.
- Octokit API area: GitHub Actions endpoints are not uniformly covered in older Octokit.NET releases; may require raw REST in the provider layer if unsupported.

---

## Phase plan (Phase 9+)

This is intentionally staged so the dashboard becomes useful early, with a performance-first chronology:

1) First show **Tier 0 + Tier 1** cards (often 0–1 API calls).
2) Then populate slower cards in the background.
3) Only later add providers that require fan-out or enrichment.

### Phase 9 — Provider scheduling + “high-yield” cards (fast dashboard)

Deliverables:
- Update the dashboard card-loading flow to implement the staged strategy above:
  - Tiered waves: `CacheOnly` fast path, then background `PreferCacheThenRefresh`.
  - Bounded parallelism for provider execution.
  - Incremental per-provider card updates (no full list clears).
- Add a provider that yields multiple useful cards from already-available data:
  - Provider: “RecentRepositoriesCardProvider” (global) using `IGitHubRepositoryService` data (prefer cache-only reads).
  - Cards: “Recently updated repos”, “Private repos count”, “Most active repo” (pick a small number; avoid turning this into a full list).

Quality gate:
- Unit tests for provider scheduling order (Tier 0/1 visible first) and cancellation.
- Verified no observable collection reset/flicker during staged updates.

### Phase 10 — Repo-scoped cards (selected repo) using existing services

Deliverables:
- Add a new provider: “RepoIssuesSummaryCardProvider” (selected repo → 1–3 cards).
- Add a new provider: “RepoRecentlyUpdatedIssuesCardProvider” (selected repo → top N issues).
- If needed, extend `IssueQuery` minimally to support the sort/filters we need.

Quality gate:
- Provider tests: deterministic ordering, stable card IDs, cancellation.
- No `ObservableCollection` reset/flicker (must use sync helper).

### Phase 11 — Global “My Work” via search (Tier 1: one search call)

Deliverables:
- Add a new abstraction: `IGitHubIssueSearchService` (or fold into a new “My Work” service).
- Provider: “MyAssignedIssuesCardProvider” (top N).
- Provider: “MyReviewRequestsCardProvider” (PRs via search).

Notes:
- Using search first avoids needing repo-by-repo crawling.
- Add explicit rate-limit/backoff handling in the provider layer if needed.

Quality gate:
- Cache key correctness tests (query normalization).
- “No-results” states are stable and do not spam the UI.

### Phase 12 — Notifications (Inbox card)

Deliverables:
- Add `IGitHubNotificationService` + Octokit implementation.
- Provider: “NotificationsCardProvider”.
- (Optional) Add mutation: mark-as-read, guarded behind safe UX and tests.

Quality gate:
- Polling cancellation coverage (no orphan timers).
- UITest: dashboard shows notifications card surface (test-nav path).

### Phase 13 — Activity cards (user + repo events)

Deliverables:
- Add `IGitHubActivityService` + Octokit implementation.
- Provider: “RecentActivityCardProvider” (global) and “RepoActivityCardProvider” (selected repo).

Quality gate:
- Ensure activity payloads map to small, UI-friendly models (don’t expose raw event types to UI).

### Phase 14 — Enrich repo details (snapshot card)

Deliverables:
- Extend repository model(s) or add a details service.
- Provider: “RepoSnapshotCardProvider”.

Quality gate:
- Backwards compatible model changes (avoid breaking existing pages/tests).

---

## Implementation guidance (to keep code consistent)

- Prefer one abstraction per “data family”:
  - Issues, PRs, Notifications, Activity, Releases…
- In `JitHub.GitHub.Octokit`:
  - Extend `IGitHubDataSource` with narrowly scoped methods.
  - Implement cached services using `CacheRuntime` and `GitHubCacheKeys` conventions.
- In the app:
  - Providers should not own cache keys directly; providers call services.
  - Card IDs should be stable across refreshes (e.g., hash of provider + entity ID).

---

## Open questions (need one decision before implementing Phase 10–12)

1) **Do we want cross-repo “My Work” now, or keep everything repo-scoped initially?**
- Cross-repo requires search queries and careful scoping (rate limits, permissions).

2) **Are we OK with PRs initially coming from Search as `is:pr` results?**
- This is often the fastest route, but we may later want PR-specific data (checks/mergeability).

3) **Notification mutations (mark as read)**
- We can ship read-only cards first; mutations add complexity (error handling, optimistic UI).
