namespace JitHubV3.Services.Ai;

public interface IAiEnablementStore
{
    ValueTask<bool> GetIsEnabledAsync(CancellationToken ct);

    ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct);
}
