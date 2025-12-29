namespace JitHub.Data.Caching;

public interface ICacheEventBus
{
    IDisposable Subscribe(Action<CacheEvent> handler);

    void Publish(CacheEvent cacheEvent);
}
