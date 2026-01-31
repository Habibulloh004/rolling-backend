using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Notifications.Commands;
using Rolling.Domain.Notifications;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Persistence.Postgres;

public sealed class PostgresNotificationRepository
{
    private readonly AppDbContext _dbContext;

    public PostgresNotificationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Notification> SaveAsync(CreateNotificationCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = new NotificationRecord
        {
            EnTitle = command.EnTitle,
            EnBody = command.EnBody,
            RuTitle = command.RuTitle,
            RuBody = command.RuBody,
            UzTitle = command.UzTitle,
            UzBody = command.UzBody,
            CreatedAt = DateTime.UtcNow
        };

        await _dbContext.Notifications.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Return with English as default for the created response
        return Notification.Restore(entity.Id, entity.EnTitle, entity.EnBody, entity.CreatedAt);
    }

    public async Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, string lang, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = Math.Clamp(take, 1, 1000);

        var items = await _dbContext.Notifications
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(length)
            .ToListAsync(cancellationToken);

        return items
            .Select(entity => Notification.Restore(
                entity.Id,
                entity.GetTitle(lang),
                entity.GetBody(lang),
                entity.CreatedAt))
            .ToList();
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await _dbContext.Notifications.FindAsync(new object[] { id }, cancellationToken);
        if (entity is null)
            return false;

        _dbContext.Notifications.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<NotificationRecord>> GetAllRecentAsync(int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = Math.Clamp(take, 1, 1000);

        return await _dbContext.Notifications
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(length)
            .ToListAsync(cancellationToken);
    }
}
