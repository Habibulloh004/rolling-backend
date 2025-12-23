using System.Linq;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Domain.Notifications;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Persistence.Postgres;

public sealed class PostgresNotificationRepository : INotificationRepository
{
    private readonly AppDbContext _dbContext;

    public PostgresNotificationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAsync(Notification notification, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = NotificationEvent.FromDomain(notification);
        await _dbContext.NotificationEvents.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = Math.Clamp(take, 1, 1000);
        var items = await _dbContext.NotificationEvents
            .AsNoTracking()
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(length)
            .ToListAsync(cancellationToken);

        return items
            .Select(entity => entity.ToDomain())
            .ToList();
    }
}
