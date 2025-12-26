# JitHubV3 — GitHub OAuth (Server-Mediated) Migration + Architecture

## Executive Summary
We are migrating the old WinAppSDK app’s authentication approach into the new Uno Platform solution:

- **Client** (Uno app) initiates OAuth sign-in.
- **Server** (JitHubV3.Server / Azure Function equivalent) performs the GitHub OAuth **code → token** exchange so the **GitHub client secret never ships** in the app binary.
- **Client** stores the resulting token using a platform-appropriate secure vault (where possible) and uses **Octokit** via an abstraction to call GitHub APIs.

**Definition of Done (for the next implementation phase):** A user can log in on each supported Uno platform and successfully execute one authenticated GitHub call (e.g., list private repos) via the official GitHub SDK (Octokit) behind an abstraction.

---

## Context & Current Repo State
### New Uno App (JitHubV3)
- Uses Uno.Extensions hosting, navigation, and authentication.
- Auth is already wired at app bootstrap:
  - `UseAuthentication(auth => auth.AddWeb(name: "WebAuthentication"))` in [JitHubV3/App.xaml.cs](../JitHubV3/App.xaml.cs)
  - Initial flow is `RefreshAsync()` → navigate to either `MainViewModel` or `LoginViewModel`.

### New Server (JitHubV3.Server)
- Minimal host that serves the WASM static assets and exposes a sample endpoint:
  - [JitHubV3.Server/Program.cs](../JitHubV3.Server/Program.cs)
- No GitHub OAuth endpoints exist yet.

### Old App (JitHub_old) — How Auth Works Today
The old app already implements a “server-mediated OAuth” design.

#### Old Client Login Trigger
- Login UI binds to a command that calls `IAccountService.Authenticate(scopes)`:
  - [JitHub_old/JitHub/Pages/LoginPage.xaml](../JitHub_old/JitHub/Pages/LoginPage.xaml)
  - [JitHub_old/JitHub/ViewModels/LoginPageViewModel.cs](../JitHub_old/JitHub/ViewModels/LoginPageViewModel.cs)

#### Old Client Launches Browser to Server
- `LocalAccountService.Authenticate(...)` launches:
  - `https://localhost:7040/authenticate?scopes=...`
  - [JitHub_old/JitHub.Services.Accounts/LocalAccountsService.cs](../JitHub_old/JitHub.Services.Accounts/LocalAccountsService.cs)

#### Old Web App Redirects to GitHub then Back to Custom Scheme
- Web component redirects to GitHub login URL (`OauthLoginUrl`):
  - [JitHub_old/JitHub.Web/JitHub.Web.Client/RedirectToGitHub.razor](../JitHub_old/JitHub.Web/JitHub.Web.Client/RedirectToGitHub.razor)
- After token acquisition, web component navigates to:
  - `jithub://auth?token=...&clientId=...&userId=...`
  - [JitHub_old/JitHub.Web/JitHub.Web.Client/RedirectToJitHub.razor](../JitHub_old/JitHub.Web/JitHub.Web.Client/RedirectToJitHub.razor)

#### Old Client Token Storage
- Uses `Windows.Security.Credentials.PasswordVault` and stores `USER_ID` in local settings:
  - [JitHub_old/JitHub.Services.Accounts/LocalAccountsService.cs](../JitHub_old/JitHub.Services.Accounts/LocalAccountsService.cs)
  - Settings abstraction: [JitHub_old/JitHub.Services.Common/SettingsService.cs](../JitHub_old/JitHub.Services.Common/SettingsService.cs)

#### Old Octokit Usage
- Old “GitHub service” (incomplete) uses **Octokit.GraphQL** and is authorized via a callback when the account service gets a token:
  - [JitHub_old/JitHub.Services.GitHubService/GitHubService.cs](../JitHub_old/JitHub.Services.GitHubService/GitHubService.cs)

---

## Issues / Risks Identified in Old Auth Flow
1. **Scope delimiter mismatch**
   - Old desktop client joins scopes with `;`.
   - Old web layer appears to split scopes with a different delimiter (commonly `:` in older codebases).
   - Result: user may end up authorizing an invalid scope string.

2. **Token in URL query string**
   - Old flow puts the GitHub access token in the `jithub://...?...` query.
   - Risks include OS/browser history, logging, crash reports, and URI leakage.

3. **Platform portability**
   - Old client relies on WinAppSDK protocol activation and Windows `PasswordVault`.
   - Uno targets require a cross-platform broker and a cross-platform storage strategy.

---

## Target Architecture (High Level)
We implement authentication and GitHub access as **two independent layers**:

1. **Authentication**
   - Client initiates OAuth login via server.
   - Server holds the GitHub client secret and performs token exchange.
   - Client receives a short-lived “handoff code” (not the token) and exchanges it over HTTPS.

2. **GitHub API**
   - Client code depends on `IGitHubApi` (our abstraction).
   - Octokit becomes an implementation detail (`OctokitGitHubApi`).

This keeps the solution flexible if we later:
- switch from OAuth App to GitHub App,
- replace Octokit with direct REST calls,
- proxy GitHub calls through the server for specific platforms.

---

## Proposed Components & Responsibilities

### Client (Uno) Components

#### UI Layer
- `LoginPage` + `LoginViewModel`: only responsible for initiating login and showing basic status.
- `MainPage` + `MainViewModel`: shows authenticated state and triggers a proof call.

#### Auth Layer
- `IAuthSession`
  - exposes `IsAuthenticated`, `UserId` (optional), and access token retrieval.
- `ITokenStore`
  - persists tokens securely when supported.

**Uno guidance to align with:**
- Web Authentication Broker: https://platform.uno/docs/articles/features/web-authentication-broker.html
- PasswordVault (cross-platform behavior & limitations): https://platform.uno/docs/articles/features/PasswordVault.html
- Uno.Extensions Authentication overview: https://platform.uno/docs/articles/external/uno.extensions/doc/Learn/Authentication/AuthenticationOverview.html

#### GitHub API Layer
- `IGitHubApi`
  - `Task<IReadOnlyList<RepositoryDto>> GetMyRepositoriesAsync(bool includePrivate, CancellationToken ct)`
- `IOctokitClientFactory`
  - `GitHubClient Create(string token)`

> Note: We will likely use Octokit REST (`Octokit.GitHubClient`) for listing repos.

### Server Components (JitHubV3.Server)

#### OAuth Endpoints
We add a minimal OAuth surface that the Uno client can call.

- `GET /authenticate` (UI entrypoint)
- `GET /auth/start` (API endpoint that redirects to GitHub)
  - Inputs: desired scopes, platform hint, return URI hint (optional)
  - Output: redirect to GitHub authorization endpoint
- `GET /auth/callback`
  - Receives GitHub `code` (and `state`)
  - Exchanges code for token using `client_secret`
  - Creates a **short-lived handoff code** and redirects to the app callback
- `POST /auth/exchange`
  - Inputs: `handoffCode` (one-time)
  - Output: token payload (JSON) + user info

#### Optional API Proxy (for WebAssembly security)
For WebAssembly, secure token storage is not truly possible in-browser. If “secure on every platform” includes WASM, we should avoid returning a long-lived access token to the browser. Two options:

- **Option A (Recommended for WASM security):**
  - Store GitHub token server-side (encrypted at rest) and return an **HttpOnly secure cookie** session.
  - Expose GitHub API endpoints on server which internally use Octokit.
  - Client uses `IGitHubApi` implementation that calls server.

- **Option B (Less secure, client-only):**
  - Store token in browser storage (not a secure enclave).
  - Only acceptable if product explicitly accepts browser-risk.

We can support both by selecting the `IGitHubApi` implementation per target.

---

## Detailed Flows (Sequence)

### Flow 1 — Login Start (Client → Server → GitHub)
1. Client calls `IAuthenticationService.LoginAsync(...)` (Uno.Extensions) or a dedicated login service.
2. Client opens a browser (or broker) window to `GET {ServerBaseUrl}/authenticate?...`.
  - The UI page immediately forwards to `/auth/start` to begin the OAuth redirect.
3. Server redirects to `https://github.com/login/oauth/authorize?...`.
4. User signs in and authorizes.

### Flow 2 — Callback (GitHub → Server → Client)
5. GitHub redirects to `GET {ServerBaseUrl}/auth/callback?code=...&state=...`.
6. Server validates `state`, exchanges `code → token` using `client_id` + `client_secret`.
7. Server creates a one-time `handoffCode` (TTL e.g. 60–120 seconds).
8. Server redirects to the client callback:

- Native (Android/iOS/macOS/Windows/Linux): custom scheme
  - e.g. `jithubv3:/authentication-callback?handoffCode=...`
  - Uno WebAuthentication Broker notes:
    - iOS/macOS must register the custom scheme in `Info.plist`.
- WebAssembly: origin-based callback
  - e.g. `https://{host}/authentication-callback?handoffCode=...`
  - Uno docs: WebAssembly redirect URI must match the app origin.

### Flow 3 — Exchange (Client ↔ Server)
9. Client receives `handoffCode` and calls `POST /auth/exchange`.
10. Server returns token + user metadata.
11. Client stores token via `ITokenStore` where supported.

### Flow 4 — Proof GitHub Call
12. Client calls `IGitHubApi.GetMyRepositoriesAsync(includePrivate:true)`.
13. `OctokitGitHubApi` uses the token to call GitHub and returns repo list.

---

## Security Model

### Threats We Protect Against
- Client binary reverse engineering revealing `client_secret`.
- Token leakage through logs/history/telemetry.
- CSRF / OAuth response injection.

### Required Controls
- Use OAuth `state` and validate it server-side.
- Never put access tokens in the custom-scheme URL.
- Handoff codes are:
  - one-time
  - short TTL
  - bound to a `state` and optionally device/session fingerprint
- Server must redact tokens from logs.
- HTTPS required for all auth endpoints.

### Token Storage (Client)
We should use the cross-platform `PasswordVault` APIs where possible.

- Official Uno docs: PasswordVault is supported on Windows/Android/iOS, but **not on WebAssembly**.
  - https://platform.uno/docs/articles/features/PasswordVault.html

**Implication:** If “secure on any platform” includes WebAssembly, we must not store long-lived tokens in the browser; prefer server-side session and API proxy for WASM.

---

## Data Contracts (Proposed)

### `POST /auth/exchange` request
```json
{
  "handoffCode": "string"
}
```

### `POST /auth/exchange` response (native client mode)
```json
{
  "accessToken": "string",
  "tokenType": "bearer",
  "scopes": ["repo", "user"],
  "user": {
    "id": 123456,
    "login": "octocat"
  }
}
```

### Repositories DTO (client-facing)
```json
{
  "id": 123,
  "name": "MyRepo",
  "owner": "me",
  "isPrivate": true,
  "htmlUrl": "https://github.com/me/MyRepo"
}
```

---

## Service Registration & Project Placement (Proposed)

### Client (JitHubV3)
- `Presentation/`:
  - ViewModels (`LoginViewModel`, `MainViewModel`)
- `Services/Auth/`:
  - `ITokenStore`, `PasswordVaultTokenStore`, `AuthSession`
- `Services/GitHub/`:
  - `IGitHubApi`, `OctokitGitHubApi`, `OctokitClientFactory`
- `Services/ServerApi/`:
  - typed client for `POST /auth/exchange` and optional proxy endpoints

Registration would occur in the existing `.ConfigureServices(...)` in [JitHubV3/App.xaml.cs](../JitHubV3/App.xaml.cs).

### Server (JitHubV3.Server)
- `Apis/Auth/`:
  - Minimal API endpoints for `/auth/start`, `/auth/callback`, `/auth/exchange`
  - Minimal UI endpoints for `/authenticate` and `/auth/complete` (Windows protocol handoff)
- `Services/GitHubOAuth/`:
  - `IGitHubOAuthService` which wraps Octokit OAuth exchange
- `Services/Handoff/`:
  - in-memory store for handoff codes (dev) + distributed store (prod) later

---

## Platform Notes (Uno)

### Web Authentication Broker
- Uno docs: https://platform.uno/docs/articles/features/web-authentication-broker.html
- Key constraints:
  - iOS/macOS: redirect URI **must** be a custom scheme, registered in app manifest (Info.plist).
  - WebAssembly: redirect URI **must** be the application origin.

### Custom Protocol Activation
If we need custom scheme activation in native targets beyond the broker callback, Uno provides protocol activation guidance:
- https://platform.uno/docs/articles/features/protocol-activation.html

---

## Migration Plan (Implementation Checklist)

### Phase 0 — Confirm Targets & URLs

#### Phase 0A — Concrete dev URLs (current repo defaults)
These values come from the `JitHubV3.Server` launch profile and are the recommended **first** targets for local development:

- Server base URLs
  - HTTPS: `https://localhost:5002`

#### Phase 0B — Create GitHub OAuth App (recommended dev settings)
Create an OAuth App in your GitHub profile and set:

- **Homepage URL**: `https://localhost:5002` (your current setting)
- **Authorization callback URL**: `https://localhost:5002/auth/callback` (your current setting)
- **Client ID**: `Ov23libqduSlPx5TcCne`

Notes:
- We intentionally set the GitHub callback to the **server** (`/auth/callback`). The server then redirects back to the Uno app’s broker callback.
- For local device/emulator testing, `localhost` may not resolve to the dev machine from the device. See “Device & emulator access” below.

#### Phase 0C — Decide initial “secure login” platforms
The client currently targets:

- `net10.0-windows10.0.26100` (WinAppSDK)
- `net10.0-desktop` (Skia Desktop)
- `net10.0-android`
- `net10.0-ios`
- `net10.0-browserwasm`

For the first milestone, we will **exclude WebAssembly** and target native/desktop only. Security differs:

- Native (Windows/Android/iOS/Desktop): we can store tokens using `PasswordVault` (platform secure stores).
- WebAssembly: there is no secure equivalent for persisting secrets in-browser; we’ll handle this in a later phase.

#### Phase 0D — App callback URIs (for the broker)
The Uno Web Authentication Broker uses a redirect URI of the form:

- Native: `<scheme>:/authentication-callback` (custom scheme; must be registered per platform)
- WebAssembly: must be an origin URL (protocol + host + port)

Recommended initial scheme name for native targets:

- Scheme: `jithubv3`
- Redirect: `jithubv3:/authentication-callback`

We will configure platform registration in Phase 2.

#### Phase 0E — Device & emulator access (important)
If you run the server on your dev machine:

- **Windows/desktop browsers**: `https://localhost:5002` works.
- **Android emulator**: `localhost` refers to the emulator, not your PC. Typically you use `http://10.0.2.2:5001` for host loopback; HTTPS with a dev cert often requires extra work.
- **Physical devices**: you generally need a reachable host name/IP (same Wi‑Fi) or a dev tunnel.

If we need iOS/Android physical device sign-in early, plan on using a stable HTTPS endpoint (dev tunnel or deployed auth host) and create a separate GitHub OAuth App callback like `https://<your-dev-host>/auth/callback`.

### Phase 1 — Server OAuth Endpoints
- Implement `/authenticate` (UI), `/auth/start`, `/auth/callback`, `/auth/exchange` in `JitHubV3.Server`.
- Store `GitHub:ClientId` and `GitHub:ClientSecret` in server configuration (env vars, user secrets, or managed secret store).

### Phase 2 — Client Login Integration
- Wire `LoginViewModel` to call Uno authentication web provider / broker.
- Handle broker result and call `/auth/exchange`.

### Phase 3 — Secure Token Storage
- Implement `PasswordVaultTokenStore`.
- WASM strategy decision:
  - either server session + proxy endpoints, or accept browser storage risk.

### Phase 4 — GitHub API Abstraction + Proof Call
- Add `IGitHubApi` and an Octokit implementation.
- Implement “list my repos including private” as the first proof call.

### Phase 5 — UX & Validation
- Ensure login/logout flow is smooth and consistent with Uno navigation.
- Validate with Uno runtime tooling and platform runs.

---

## Configuration (Proposed)

### Server (JitHubV3.Server)
- `GitHub:ClientId`
- `GitHub:ClientSecret` (NEVER committed)
- `GitHub:Scopes` (default: `repo`, `user`)
- `Auth:HandoffCodeTtlSeconds`
- `Auth:AllowedRedirectOrigins` (for WASM)

### Client (JitHubV3)
- `Server:BaseUrl`
- `Auth:ProviderName` (default: `WebAuthentication`)

---

## Open Questions (Need Answers Before Implementation)
1. Which platforms are **required** for the first “secure login” milestone (include WASM or not)?
2. Should the new backend be:
   - the existing `JitHubV3.Server` project,
   - an Azure Function (like the old approach),
   - or both (server project for local dev, function for production)?
3. Token lifetime and refresh strategy:
   - GitHub OAuth tokens may not be refreshable like typical OIDC tokens.
   - Should we support re-auth on expiry only, or implement GitHub App later?

---

## Notes on Scope
This document intentionally avoids adding extra product features (repo browser UI, issues UI, etc.). The immediate objective is to migrate authentication and prove one authorized API call via Octokit behind an abstraction.