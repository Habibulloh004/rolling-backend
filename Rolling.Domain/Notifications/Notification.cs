namespace Rolling.Domain.Notifications;

public sealed class Notification
{
    private Notification(int id, string title, string message, DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Message = message;
        CreatedAt = createdAt;
    }

    public int Id { get; }
    public string Title { get; }
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }

    public static Notification Create(string title, string message, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new Notification(0, title.Trim(), message.Trim(), createdAt);
    }

    public static Notification Restore(int id, string title, string message, DateTimeOffset createdAt)
    {
        return new Notification(id, title, message, createdAt);
    }
}
