namespace JitHubV3.Services.Ai;

public interface IAiRuntimeResolver
{
    ValueTask<IAiRuntime?> ResolveSelectedRuntimeAsync(CancellationToken ct);
}

public sealed class AiRuntimeResolver : IAiRuntimeResolver
{
    private readonly IAiModelStore _modelStore;
    private readonly IReadOnlyList<IAiRuntime> _runtimes;

    public AiRuntimeResolver(IAiModelStore modelStore, IEnumerable<IAiRuntime> runtimes)
    {
        _modelStore = modelStore ?? throw new ArgumentNullException(nameof(modelStore));
        _runtimes = (runtimes ?? Enumerable.Empty<IAiRuntime>()).ToArray();
    }

    public async ValueTask<IAiRuntime?> ResolveSelectedRuntimeAsync(CancellationToken ct)
    {
        var selection = await _modelStore.GetSelectionAsync(ct).ConfigureAwait(false);
        if (selection is null || string.IsNullOrWhiteSpace(selection.RuntimeId))
        {
            return null;
        }

        return _runtimes.FirstOrDefault(r =>
            string.Equals(r.RuntimeId, selection.RuntimeId, StringComparison.OrdinalIgnoreCase));
    }
}
