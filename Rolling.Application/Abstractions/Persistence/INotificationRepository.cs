using Rolling.Domain.Notifications;

namespace Rolling.Application.Abstractions.Persistence;

public interface INotificationRepository
{
    Task SaveAsync(Notification notification, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, CancellationToken cancellationToken);
}
