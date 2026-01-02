namespace JitHubV3.Services.Platform;

public sealed partial class PlatformCapabilities : IPlatformCapabilities
{
    public bool SupportsSecureSecretStore { get; }
    public bool SupportsLocalFoundryDetection { get; }
    public bool SupportsHardwareAccelerationIntrospection { get; }

    public PlatformCapabilities()
    {
        var secure = false;
        var localFoundry = false;
        var hw = false;

        Configure(ref secure, ref localFoundry, ref hw);

        SupportsSecureSecretStore = secure;
        SupportsLocalFoundryDetection = localFoundry;
        SupportsHardwareAccelerationIntrospection = hw;
    }

    static partial void Configure(
        ref bool supportsSecureSecretStore,
        ref bool supportsLocalFoundryDetection,
        ref bool supportsHardwareAccelerationIntrospection);
}
