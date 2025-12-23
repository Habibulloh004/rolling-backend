using System.Text.Json;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Domain.Notifications;
using Rolling.Infrastructure.Configuration;
using StackExchange.Redis;

namespace Rolling.Infrastructure.Persistence.Redis;

public sealed class RedisNotificationRepository : INotificationRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly RedisOptions _options;

    public RedisNotificationRepository(IConnectionMultiplexer connectionMultiplexer, RedisOptions options)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _options = options;
    }

    public async Task SaveAsync(Notification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _connectionMultiplexer.GetDatabase();
        var document = NotificationDocument.From(notification);
        var payload = JsonSerializer.Serialize(document, SerializerOptions);
        await db.ListLeftPushAsync(_options.NotificationsKey, payload);
        var historySize = Math.Max(1, _options.HistorySize);
        await db.ListTrimAsync(_options.NotificationsKey, 0, historySize - 1);
    }

    public async Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _connectionMultiplexer.GetDatabase();
        var historySize = Math.Max(1, _options.HistorySize);
        var length = Math.Clamp(take, 1, historySize);
        var entries = await db.ListRangeAsync(_options.NotificationsKey, 0, length - 1);
        var notifications = new List<Notification>(entries.Length);

        foreach (var entry in entries)
        {
            if (entry.IsNullOrEmpty)
            {
                continue;
            }

            var document = JsonSerializer.Deserialize<NotificationDocument>(entry.ToString(), SerializerOptions);
            if (document is null)
            {
                continue;
            }

            notifications.Add(document.ToDomain());
        }

        return notifications;
    }

    private sealed record NotificationDocument(Guid Id, string Channel, string Title, string Message, DateTimeOffset CreatedAt)
    {
        public static NotificationDocument From(Notification notification) =>
            new(notification.Id, notification.Channel, notification.Title, notification.Message, notification.CreatedAt);

        public Notification ToDomain() => Notification.Restore(Id, Channel, Title, Message, CreatedAt);
    }
}
