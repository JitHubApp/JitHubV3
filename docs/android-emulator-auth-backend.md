# Android emulator — reaching the host backend for authentication

This doc is a practical runbook for testing the **JitHubV3** Uno app on an **Android emulator** while using the **local host-run backend** (`JitHubV3.Server`) for the GitHub OAuth flow.

## Why this is needed

In development the app is configured to start auth using:

- `WebAuthentication:LoginStartUri` (e.g. `https://localhost:5002/authenticate`)
- `WebAuthentication:LogoutStartUri` (e.g. `https://localhost:5002/auth/logout`)
- `WebAuthentication:CallbackUri` (e.g. `jithubv3://authentication-callback`)

On Android, `localhost` inside the emulator refers to the **emulator/device itself**, not your Windows machine. Without special routing, the emulator cannot reach a server listening on the host’s `localhost`.

## Recommended approach: `adb reverse` (keep `localhost`)

Using `adb reverse` makes the emulator’s `localhost:<port>` forward to your Windows machine’s `localhost:<port>`.

Why it’s the best default here:

- You can keep the existing `localhost` URLs in `JitHubV3/appsettings.development.json`.
- GitHub OAuth callback URLs stay stable (they are based on the server host used for `/auth/start` → `/auth/callback`).
- You usually **avoid firewall + IP binding** issues because traffic is tunneled over ADB.

### 1) Start the backend on a known port

Check the server launch profile in:

- `JitHubV3.Server/Properties/launchSettings.json`

By default it is configured to run on `https://localhost:5002`.

### 2) Decide on HTTPS vs HTTP (dev)

**HTTPS (current default)**
- Works if the emulator trusts the dev certificate.
- Often fails with TLS errors on Android emulators unless you explicitly install/trust the cert.

**HTTP (often easiest for emulator testing)**
- Add an HTTP URL to the server launch profile (for Development).
- Update the app dev config to use `http://localhost:<port>`.
- Allow cleartext HTTP on Android (see below).

> Note: The server already skips `UseHttpsRedirection()` in Development (see `JitHubV3.Server/Program.cs`), so running on HTTP is an acceptable dev-only configuration.

### 3) Allow HTTP on Android (only if you choose HTTP)

If you switch to HTTP, Android will block cleartext traffic by default.

Set this in:

- `JitHubV3/Platforms/Android/AndroidManifest.xml`

Add the attribute on the `<application>` element:

```xml
<application
  android:allowBackup="true"
  android:supportsRtl="true"
  android:usesCleartextTraffic="true">
</application>
```

### 4) Configure the app to use localhost (dev)

Dev config is in:

- `JitHubV3/appsettings.development.json`

Typical values:

- `WebAuthentication:LoginStartUri = http(s)://localhost:<port>/authenticate`
- `WebAuthentication:LogoutStartUri = http(s)://localhost:<port>/auth/logout`
- `WebAuthentication:CallbackUri = jithubv3://authentication-callback`

The login flow uses:

- Browser/broker start: `/authenticate?client=native&redirect_uri=jithubv3://authentication-callback&scope=...`
- Token exchange: `POST /auth/exchange` (derived from the same server authority)

Implementation reference:

- `JitHubV3/Authentication/GitHubAuthFlow.cs`

### 5) Set up the ADB reverse tunnel

In PowerShell:

```powershell
adb devices
adb reverse tcp:<port> tcp:<port>
adb reverse --list
```

Use the same `<port>` you’re running the backend on (e.g., `5002` or `5001`).

### 6) Validate quickly (optional, but helpful)

From the emulator browser, verify you can load the authenticate entrypoint:

- `http(s)://localhost:<port>/authenticate`

Then try sign-in from the app.

## Alternative approach: use the emulator host gateway (`10.0.2.2`)

Android emulators expose the host machine as `10.0.2.2`.

This can work, but it usually requires more moving pieces:

- The backend must listen on a non-loopback interface (e.g., `0.0.0.0`) or be reachable via the machine’s LAN IP.
- Windows firewall rules may need to allow inbound connections.
- HTTPS is harder: the emulator must trust your dev certificate.
- **GitHub OAuth callback URL changes**, because the server builds its GitHub callback using the incoming host:
  - If you start at `http(s)://10.0.2.2:<port>/authenticate`, then GitHub will be sent a callback like `http(s)://10.0.2.2:<port>/auth/callback`.
  - Your GitHub OAuth App settings must allow that callback URL (otherwise you’ll get a `redirect_uri` mismatch).

If you choose this route, update `WebAuthentication:LoginStartUri` to `http(s)://10.0.2.2:<port>/authenticate`.

## Redirect allowlist notes

The server has redirect allowlisting logic to prevent open-redirect abuse.

Key behaviors (current implementation):

- Custom scheme redirects are allowed for native apps:
  - `jithubv3://authentication-callback`
- Desktop loopback redirects are allowed only for a strict callback path:
  - `http://127.0.0.1:<port>/oauth2/callback`
- WASM redirects are allowlisted by origin + (optionally) exact path.

Reference:

- `JitHubV3.Server/Services/Auth/OAuthRedirectPolicy.cs`
- `JitHubV3.Server/Apis/AuthApi.cs`

## Troubleshooting

### Emulator can’t reach the backend
- Confirm `adb reverse --list` includes the port mapping.
- Confirm the backend is running on the same port you reversed.
- If using HTTP on Android, confirm `android:usesCleartextTraffic="true"` is present.

### TLS / certificate errors
- Prefer HTTP for emulator dev.
- If you must use HTTPS, you’ll need to install/trust the dev certificate inside the emulator.

### GitHub says `redirect_uri` mismatch
- This means the callback URL GitHub sees (the server’s `/auth/callback`) doesn’t match what’s configured in the GitHub OAuth App.
- Using `adb reverse` with `localhost` typically avoids changing callback hosts.
- Using `10.0.2.2` or a LAN IP requires updating the GitHub OAuth App callback URL accordingly.

### App gets a callback but `/auth/exchange` fails
- `GitHubAuthFlow` posts to `/auth/exchange` on the same authority as the start URI.
- If `/authenticate` loaded but exchange fails, double-check:
  - port mapping (`adb reverse`)
  - scheme/port consistency between `LoginStartUri` and the server’s actual listening URL
