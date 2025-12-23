using Rolling.Domain.Notifications;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class NotificationEvent
{
    public Guid Id { get; init; }
    public string Channel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }

    public static NotificationEvent FromDomain(Notification notification) =>
        new()
        {
            Id = notification.Id,
            Channel = notification.Channel,
            Title = notification.Title,
            Message = notification.Message,
            CreatedAt = notification.CreatedAt
        };

    public Notification ToDomain() => Notification.Restore(Id, Channel, Title, Message, CreatedAt);
}
