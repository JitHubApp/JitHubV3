namespace JitHubV3.Server.Services.Auth;

/// <summary>
/// String constants used by clients to tell the server which auth flow is being used.
/// Keep these stable: server and apps must agree.
/// </summary>
internal static class AuthClientKinds
{
    internal const string Wasm = "wasm";

    // WASM E2E/dev mode: full-page navigation to backend, backend redirects back with token in URL fragment.
    internal const string WasmFullPage = "wasm-fullpage";

    // Windows App SDK: system browser + protocol activation back into the app.
    internal const string Windows = "windows";

    // Desktop (WinAppSDK + Skia): system browser + loopback redirect (http://127.0.0.1:{port}/...).
    internal const string Desktop = "desktop";

    // iOS/Android/macOS/etc: broker-based flow using redirect_uri.
    internal const string Native = "native";
}
