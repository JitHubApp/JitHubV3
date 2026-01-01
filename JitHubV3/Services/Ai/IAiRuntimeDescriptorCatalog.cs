namespace JitHubV3.Services.Ai;

/// <summary>
/// Returns all known AI runtime descriptors (declared capabilities), regardless of current configuration.
/// Use <see cref="IAiRuntimeCatalog"/> for runtimes that are usable right now.
/// </summary>
public interface IAiRuntimeDescriptorCatalog
{
    Task<IReadOnlyList<AiRuntimeDescriptorInfo>> GetDeclaredRuntimesAsync(CancellationToken ct);
}

public sealed record AiRuntimeDescriptorInfo(
    string RuntimeId,
    string DisplayName,
    bool RequiresApiKey,
    bool RequiresEndpoint,
    bool SupportsLocalDownloads,
    string? Description = null);
