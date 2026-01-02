namespace JitHubV3.Services.Ai;

public sealed class AiStatusEventBus : IAiStatusEventBus, IAiStatusEventPublisher
{
    private readonly object _gate = new();
    private readonly List<Action<AiStatusEvent>> _subscribers = new();

    public IDisposable Subscribe(Action<AiStatusEvent> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        lock (_gate)
        {
            _subscribers.Add(handler);
        }

        return new Subscription(this, handler);
    }

    public void Publish(AiStatusEvent evt)
    {
        if (evt is null)
        {
            throw new ArgumentNullException(nameof(evt));
        }

        Action<AiStatusEvent>[] snapshot;
        lock (_gate)
        {
            snapshot = _subscribers.ToArray();
        }

        foreach (var s in snapshot)
        {
            try
            {
                s(evt);
            }
            catch
            {
                // isolate subscriber exceptions
            }
        }
    }

    private void Unsubscribe(Action<AiStatusEvent> handler)
    {
        lock (_gate)
        {
            _subscribers.Remove(handler);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private AiStatusEventBus? _bus;
        private Action<AiStatusEvent>? _handler;

        public Subscription(AiStatusEventBus bus, Action<AiStatusEvent> handler)
        {
            _bus = bus;
            _handler = handler;
        }

        public void Dispose()
        {
            var bus = Interlocked.Exchange(ref _bus, null);
            if (bus is null)
            {
                return;
            }

            var handler = Interlocked.Exchange(ref _handler, null);
            if (handler is null)
            {
                return;
            }

            bus.Unsubscribe(handler);
        }
    }
}
