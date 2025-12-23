namespace Rolling.Application.Abstractions.Messaging;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken = default)
        where TEvent : IIntegrationEvent;

    void Subscribe<TEvent>(IIntegrationEventHandler<TEvent> handler)
        where TEvent : IIntegrationEvent;
}
