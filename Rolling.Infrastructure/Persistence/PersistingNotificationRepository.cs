using Rolling.Application.Abstractions.Persistence;
using Rolling.Domain.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Redis;

namespace Rolling.Infrastructure.Persistence;

/// <summary>
/// Persists notifications to Redis for fast reads and to Postgres for long-term history.
/// Reads still come from Redis for speed.
/// </summary>
public sealed class PersistingNotificationRepository : INotificationRepository
{
    private readonly RedisNotificationRepository _redisRepository;
    private readonly PostgresNotificationRepository _postgresRepository;

    public PersistingNotificationRepository(
        RedisNotificationRepository redisRepository,
        PostgresNotificationRepository postgresRepository)
    {
        _redisRepository = redisRepository;
        _postgresRepository = postgresRepository;
    }

    public async Task SaveAsync(Notification notification, CancellationToken cancellationToken)
    {
        await _redisRepository.SaveAsync(notification, cancellationToken);
        await _postgresRepository.SaveAsync(notification, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var cached = await _redisRepository.GetRecentAsync(take, cancellationToken);
        if (cached.Count > 0)
        {
            return cached;
        }

        // Fall back to Postgres if Redis is empty (e.g., after restart).
        return await _postgresRepository.GetRecentAsync(take, cancellationToken);
    }
}
