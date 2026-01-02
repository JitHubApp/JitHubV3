namespace JitHubV3.Services.Ai;

public sealed class AiModelStoreEventingDecorator : IAiModelStore
{
    private readonly IAiModelStore _inner;
    private readonly IAiStatusEventPublisher _events;

    public AiModelStoreEventingDecorator(IAiModelStore inner, IAiStatusEventPublisher events)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public ValueTask<AiModelSelection?> GetSelectionAsync(CancellationToken ct)
        => _inner.GetSelectionAsync(ct);

    public async ValueTask SetSelectionAsync(AiModelSelection? selection, CancellationToken ct)
    {
        await _inner.SetSelectionAsync(selection, ct).ConfigureAwait(false);
        _events.Publish(new AiSelectionChanged(selection));
    }
}

public sealed class AiEnablementStoreEventingDecorator : IAiEnablementStore
{
    private readonly IAiEnablementStore _inner;
    private readonly IAiStatusEventPublisher _events;

    public AiEnablementStoreEventingDecorator(IAiEnablementStore inner, IAiStatusEventPublisher events)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _events = events ?? throw new ArgumentNullException(nameof(events));
    }

    public ValueTask<bool> GetIsEnabledAsync(CancellationToken ct)
        => _inner.GetIsEnabledAsync(ct);

    public async ValueTask SetIsEnabledAsync(bool isEnabled, CancellationToken ct)
    {
        await _inner.SetIsEnabledAsync(isEnabled, ct).ConfigureAwait(false);
        _events.Publish(new AiEnablementChanged(isEnabled));
    }
}
