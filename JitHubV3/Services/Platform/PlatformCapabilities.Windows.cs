namespace JitHubV3.Services.Platform;

public sealed partial class PlatformCapabilities
{
    static partial void Configure(
        ref bool supportsSecureSecretStore,
        ref bool supportsLocalFoundryDetection,
        ref bool supportsHardwareAccelerationIntrospection)
    {
        // PasswordVault-based secret store is available on Windows targets.
        supportsSecureSecretStore = true;

        // Other capabilities remain conservative until implemented.
    }
}
