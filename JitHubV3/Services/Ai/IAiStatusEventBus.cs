namespace JitHubV3.Services.Ai;

public interface IAiStatusEventBus
{
    IDisposable Subscribe(Action<AiStatusEvent> handler);
}

public interface IAiStatusEventPublisher
{
    void Publish(AiStatusEvent evt);
}
