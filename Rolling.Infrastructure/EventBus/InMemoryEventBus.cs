using System.Collections.Concurrent;
using Rolling.Application.Abstractions.Messaging;

namespace Rolling.Infrastructure.EventBus;

public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, ConcurrentBag<Subscription>> _subscriptions = new();

    public Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent
    {
        if (eventData is null)
        {
            throw new ArgumentNullException(nameof(eventData));
        }

        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers))
        {
            return Task.CompletedTask;
        }

        var tasks = handlers.Select(subscription => subscription.Invoke(eventData, cancellationToken));
        return Task.WhenAll(tasks);
    }

    public void Subscribe<TEvent>(IIntegrationEventHandler<TEvent> handler)
        where TEvent : IIntegrationEvent
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var entry = _subscriptions.GetOrAdd(typeof(TEvent), _ => new ConcurrentBag<Subscription>());
        entry.Add(new Subscription(async (evt, token) => await handler.HandleAsync((TEvent)evt, token)));
    }

    private sealed record Subscription(Func<IIntegrationEvent, CancellationToken, Task> Invoke);
}
