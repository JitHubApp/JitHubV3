# Getting Started

Welcome to the Uno Platform!

To discover how to get started with your new app: https://aka.platform.uno/get-started

For more information on how to use the Uno.Sdk or upgrade Uno Platform packages in your solution: https://aka.platform.uno/using-uno-sdk

## GitHub OAuth (local dev)

This solution uses a **server-mediated** GitHub OAuth flow.

- The server project (`JitHubV3.Server`) owns `GitHub:ClientSecret` (never shipped to the client app)
- GitHub redirects back to the server at `/auth/callback`

### Split hosting (Phase 1)

- API backend: `https://localhost:5002`

This repo currently uses native/desktop Uno heads (WinAppSDK + Skia Desktop + Android). The OAuth callback is handled via the server endpoint.

### 1) Create a GitHub OAuth App

In GitHub, create an OAuth App and set the callback URL to one (or both) of:

- `https://localhost:5002/auth/callback`


### 2) Configure secrets for `JitHubV3.Server`

Recommended (User Secrets):

- From a PowerShell prompt:
	- `cd e:\JitHubV3\JitHubV3.Server`
	- `dotnet user-secrets set "GitHub:ClientId" "<your-client-id>"`
	- `dotnet user-secrets set "GitHub:ClientSecret" "<your-client-secret>"`

Alternative (Environment variables):

- `setx GitHub__ClientId "<your-client-id>"`
- `setx GitHub__ClientSecret "<your-client-secret>"`

Restart the server after changing secrets.