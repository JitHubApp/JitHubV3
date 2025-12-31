namespace JitHubV3.Services.Ai;

public interface IAiRuntimeCatalog
{
    /// <summary>
    /// Returns runtimes that are usable on the current platform (e.g. required dependencies present).
    /// Availability may depend on configuration and secret presence.
    /// </summary>
    Task<IReadOnlyList<AiRuntimeDescriptor>> GetAvailableRuntimesAsync(CancellationToken ct);
}

public sealed class EmptyAiRuntimeCatalog : IAiRuntimeCatalog
{
    public Task<IReadOnlyList<AiRuntimeDescriptor>> GetAvailableRuntimesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AiRuntimeDescriptor>>(Array.Empty<AiRuntimeDescriptor>());
}
