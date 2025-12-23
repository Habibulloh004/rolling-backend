namespace Rolling.Application.Abstractions.Messaging;

public interface IIntegrationEventHandler<in TEvent>
    where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent eventData, CancellationToken cancellationToken);
}
