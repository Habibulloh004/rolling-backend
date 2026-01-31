using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Notifications.Commands;
using Rolling.Domain.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Redis;

namespace Rolling.Infrastructure.Persistence;

/// <summary>
/// Persists notifications to Postgres (source of truth) with Redis as cache layer.
/// Similar to how Banners work - PostgreSQL is the primary storage, Redis is cache.
/// </summary>
public sealed class PersistingNotificationRepository : INotificationRepository
{
    private readonly IRedisNotificationCache _cache;
    private readonly PostgresNotificationRepository _postgresRepository;

    public PersistingNotificationRepository(
        IRedisNotificationCache cache,
        PostgresNotificationRepository postgresRepository)
    {
        _cache = cache;
        _postgresRepository = postgresRepository;
    }

    public async Task<Notification> SaveAsync(CreateNotificationCommand command, CancellationToken cancellationToken)
    {
        // Save to PostgreSQL (source of truth)
        var notification = await _postgresRepository.SaveAsync(command, cancellationToken);

        // Invalidate cache so next read fetches fresh data
        await _cache.InvalidateNotificationCacheAsync(cancellationToken);

        return notification;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var deleted = await _postgresRepository.DeleteAsync(id, cancellationToken);
        if (deleted)
        {
            await _cache.InvalidateNotificationCacheAsync(cancellationToken);
        }

        return deleted;
    }

    public async Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, string lang, CancellationToken cancellationToken)
    {
        // Try cache first
        var cached = await _cache.GetNotificationsAsync(take, cancellationToken);
        if (cached is { Count: > 0 })
        {
            // Map from cached records with language
            return cached
                .Select(r => Notification.Restore(r.Id, r.GetTitle(lang), r.GetBody(lang), r.CreatedAt))
                .ToList();
        }

        // Cache miss - fetch all records from PostgreSQL
        var records = await _postgresRepository.GetAllRecentAsync(take, cancellationToken);

        // Warm the cache with full records
        if (records.Count > 0)
        {
            await _cache.SetNotificationsAsync(records.ToList(), cancellationToken);
        }

        // Return mapped with language
        return records
            .Select(r => Notification.Restore(r.Id, r.GetTitle(lang), r.GetBody(lang), r.CreatedAt))
            .ToList();
    }
}
