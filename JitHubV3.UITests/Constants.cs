namespace JitHubV3.UITests;

public class Constants
{
    // Default to the standalone WebAssembly UI host.
    // Override at runtime with env var `JITHUBV3_WASM_URI`.
    public readonly static string WebAssemblyDefaultUri = NormalizeUri(
        Environment.GetEnvironmentVariable("JITHUBV3_WASM_URI")
        ?? "http://localhost:5000/");
    public readonly static string iOSAppName = "com.JitHub.JitHubV3";
    public readonly static string AndroidAppName = "com.JitHub.JitHubV3";
    public readonly static string iOSDeviceNameOrId = "iPad Pro (12.9-inch) (3rd generation)";

    public readonly static Platform CurrentPlatform = Platform.Browser;
    public readonly static Browser WebAssemblyBrowser = Browser.Chrome;

    private static string NormalizeUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return "http://localhost:5000/";
        }

        return uri.EndsWith("/", StringComparison.Ordinal) ? uri : uri + "/";
    }
}
