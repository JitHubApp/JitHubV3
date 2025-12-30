# Dashboard implementation (JitHubV3)

This document describes how the dashboard is implemented in the app: the viewmodel orchestration, provider scheduling, caching rules, and the current provider catalog.

## High-level architecture

- The dashboard UI is driven by `DashboardViewModel`.
- Cards are produced by providers implementing `IDashboardCardProvider` / `IStagedDashboardCardProvider`.
- GitHub data access is **always** behind `JitHub.GitHub.Abstractions` services (no Octokit types in UI).
- Cached services and their Octokit-backed implementations live behind the service layer.

## Provider execution and refresh policy

### Refresh modes
Providers are called with a `RefreshMode`:
- `CacheOnly` is used for fast initial render (no waiting for network).
- Background refresh uses `PreferCacheThenRefresh` to keep UI responsive while updating.

Providers must:
- Honor `CancellationToken` (dashboard deactivation/context changes).
- Tolerate “no cached value yet” (show empty-state cards or return no cards).

### Staged execution tiers
Providers declare a `DashboardCardProviderTier` used by the scheduler to stage work.

Rule of thumb:
- Use `Local` for pure context cards.
- Use `SingleCallMultiCard` for “feed-style” providers where a single request yields multiple cards.
- Use `SingleCallSingleCard` for one request → one card.
- Use `MultiCallEnrichment` only when fan-out/enrichment is unavoidable.

## Card identity rules (`CardId`)

`DashboardViewModel` synchronizes the UI card collection by `CardId`. Therefore:
- **All visible cards must have unique `CardId`s.** Collisions will cause cards to overwrite each other.
- “Feed-style” cards must use **stable** per-item ids (do not use `string.GetHashCode()`).

### Shared helper
Feed-style providers use `JitHubV3.Presentation.DashboardCardId` to generate stable per-item card ids using a deterministic hash.

## Current providers (catalog)

Global scope:
- Selected repo card
- Recent repositories
- Notifications (feed-style: one card per notification; empty-state card when none)
- My recent activity (feed-style: one card per event; empty-state card when none)

Selected-repo scope:
- Repo issues summary
- Repo recently updated issues
- Repo snapshot
- Repo recent activity (feed-style: one card per event; empty-state card when none)

## Dependency injection

Providers and their backing services are registered in the app startup/hosting configuration.

Rules:
- Providers depend on abstraction services (`IGitHub*Service`), never on Octokit directly.
- Cache keys and cached services should live in the service layer, not in the UI.

## Tests

Unit tests live in `JitHubV3.Tests`.

Provider tests validate:
- Empty-state behavior
- Cancellation behavior
- Multi-card providers produce one card per item
- Stable `CardId` behavior where relevant
