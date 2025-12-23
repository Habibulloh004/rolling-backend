namespace Rolling.Application.Notifications.Commands;

public sealed record CreateNotificationCommand(string Channel, string Title, string Message);
