namespace Rolling.Domain.Notifications;

public sealed class Notification
{
    private Notification(Guid id, string channel, string title, string message, DateTimeOffset createdAt)
    {
        Id = id;
        Channel = channel;
        Title = title;
        Message = message;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }
    public string Channel { get; }
    public string Title { get; }
    public string Message { get; }
    public DateTimeOffset CreatedAt { get; }

    public static Notification Create(string channel, string title, string message, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new Notification(Guid.NewGuid(), channel.Trim(), title.Trim(), message.Trim(), createdAt);
    }

    public static Notification Restore(Guid id, string channel, string title, string message, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Identifier must be provided", nameof(id));
        }

        return new Notification(id, channel, title, message, createdAt);
    }
}
