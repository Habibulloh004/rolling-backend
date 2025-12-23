using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Models.News;

public sealed class CreateNewsRequest
{
    public LocalizedNotificationPayload? En { get; set; }
    public LocalizedNotificationPayload? Ru { get; set; }
    public LocalizedNotificationPayload? Uz { get; set; }
    public string MessageType { get; set; } = "body";
    public Dictionary<string, NotificationRecordPayload>? CustomMessages { get; set; }
    public Dictionary<string, string>? Data { get; set; }

    public NotificationRecord ToEntity()
    {
        var en = En ?? new LocalizedNotificationPayload();
        var ru = Ru ?? new LocalizedNotificationPayload();
        var uz = Uz ?? new LocalizedNotificationPayload();

        return new NotificationRecord
        {
            EnTitle = en.Title,
            EnBody = en.Body,
            RuTitle = ru.Title,
            RuBody = ru.Body,
            UzTitle = uz.Title,
            UzBody = uz.Body
        };
    }
}

public sealed class LocalizedNotificationPayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public sealed class NotificationRecordPayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}
