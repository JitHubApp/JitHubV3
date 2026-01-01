using JitHubV3.Services.Ai;

namespace JitHubV3.Tests.Ai;

public sealed class TestAiRuntimeSettingsStore : IAiRuntimeSettingsStore
{
    public AiRuntimeSettings Settings { get; set; } = new();

    public ValueTask<AiRuntimeSettings> GetAsync(CancellationToken ct)
        => ValueTask.FromResult(Settings ?? new AiRuntimeSettings());

    public ValueTask SetAsync(AiRuntimeSettings settings, CancellationToken ct)
    {
        Settings = settings ?? new AiRuntimeSettings();
        return ValueTask.CompletedTask;
    }
}
