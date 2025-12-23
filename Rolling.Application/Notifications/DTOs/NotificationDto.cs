namespace Rolling.Application.Notifications.DTOs;

public sealed record NotificationDto(Guid Id, string Channel, string Title, string Message, DateTimeOffset CreatedAt);
