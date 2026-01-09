using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using StackExchange.Redis;

namespace Rolling.Infrastructure.Persistence.Redis;

public interface IRedisNotificationCache
{
    Task<List<NotificationRecord>?> GetNotificationsAsync(int take, CancellationToken cancellationToken = default);
    Task SetNotificationsAsync(List<NotificationRecord> notifications, CancellationToken cancellationToken = default);
    Task InvalidateNotificationCacheAsync(CancellationToken cancellationToken = default);
}

public sealed class RedisNotificationCache : IRedisNotificationCache
{
    private const string NotificationsAllKey = "notifications:all";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisNotificationCache> _logger;

    public RedisNotificationCache(IConnectionMultiplexer redis, ILogger<RedisNotificationCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<List<NotificationRecord>?> GetNotificationsAsync(int take, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();

            var cached = await db.StringGetAsync(NotificationsAllKey);
            if (cached.IsNullOrEmpty)
            {
                _logger.LogDebug("Cache MISS: Notifications");
                return null;
            }

            var notifications = JsonSerializer.Deserialize<List<NotificationDocument>>(cached.ToString(), SerializerOptions);
            if (notifications == null)
            {
                _logger.LogWarning("Failed to deserialize cached notifications");
                return null;
            }

            _logger.LogDebug("Cache HIT: Notifications (count: {Count})", notifications.Count);

            var result = notifications
                .OrderByDescending(n => n.CreatedAt)
                .Take(Math.Clamp(take, 1, 500))
                .Select(d => d.ToEntity())
                .ToList();

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting notifications from Redis cache");
            return null;
        }
    }

    public async Task SetNotificationsAsync(List<NotificationRecord> notifications, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();

            var documents = notifications.Select(NotificationDocument.FromEntity).ToList();
            var json = JsonSerializer.Serialize(documents, SerializerOptions);

            await db.StringSetAsync(NotificationsAllKey, json);
            _logger.LogDebug("Cached: Notifications (count: {Count})", notifications.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching notifications to Redis");
        }
    }

    public async Task InvalidateNotificationCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();

            await db.KeyDeleteAsync(NotificationsAllKey);
            _logger.LogDebug("Invalidated notification cache");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating notification cache");
        }
    }

    private sealed record NotificationDocument(
        int Id,
        string EnTitle,
        string EnBody,
        string RuTitle,
        string RuBody,
        string UzTitle,
        string UzBody,
        DateTime CreatedAt)
    {
        public static NotificationDocument FromEntity(NotificationRecord entity) => new(
            entity.Id,
            entity.EnTitle,
            entity.EnBody,
            entity.RuTitle,
            entity.RuBody,
            entity.UzTitle,
            entity.UzBody,
            entity.CreatedAt
        );

        public NotificationRecord ToEntity() => new()
        {
            Id = Id,
            EnTitle = EnTitle,
            EnBody = EnBody,
            RuTitle = RuTitle,
            RuBody = RuBody,
            UzTitle = UzTitle,
            UzBody = UzBody,
            CreatedAt = CreatedAt
        };
    }
}
