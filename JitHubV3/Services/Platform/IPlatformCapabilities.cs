namespace JitHubV3.Services.Platform;

public interface IPlatformCapabilities
{
    bool SupportsSecureSecretStore { get; }
    bool SupportsLocalFoundryDetection { get; }
    bool SupportsHardwareAccelerationIntrospection { get; }
}
