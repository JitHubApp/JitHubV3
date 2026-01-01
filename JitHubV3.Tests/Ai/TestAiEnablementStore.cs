using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class TestAiEnablementStore : IAiEnablementStore
{
    public bool IsEnabled { get; set; } = true;

    public ValueTask<bool> GetIsEnabledAsync(CancellationToken ct) => ValueTask.FromResult(IsEnabled);

    public ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct)
    {
        IsEnabled = isEnabled;
        return ValueTask.CompletedTask;
    }
}
