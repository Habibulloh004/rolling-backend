namespace Rolling.Application.Notifications.DTOs;

public sealed record NotificationDto(int Id, string Title, string Message, DateTimeOffset CreatedAt);
