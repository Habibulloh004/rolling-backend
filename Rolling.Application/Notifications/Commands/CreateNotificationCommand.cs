namespace Rolling.Application.Notifications.Commands;

public sealed record CreateNotificationCommand(
    string EnTitle,
    string EnBody,
    string RuTitle,
    string RuBody,
    string UzTitle,
    string UzBody
);
