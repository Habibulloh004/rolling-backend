namespace Rolling.Application.Abstractions.Messaging;

public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredOn { get; }
}
