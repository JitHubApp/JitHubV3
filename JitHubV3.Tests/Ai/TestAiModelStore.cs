using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class TestAiModelStore : IAiModelStore
{
    public AiModelSelection? Selection { get; set; }

    public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Selection);
    }

    public ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Selection = selection;
        return ValueTask.CompletedTask;
    }
}
