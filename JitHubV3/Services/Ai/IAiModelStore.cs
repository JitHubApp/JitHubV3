namespace JitHubV3.Services.Ai;

public interface IAiModelStore
{
    ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct);

    ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct);
}
