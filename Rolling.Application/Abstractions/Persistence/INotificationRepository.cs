using Rolling.Application.Notifications.Commands;
using Rolling.Domain.Notifications;

namespace Rolling.Application.Abstractions.Persistence;

public interface INotificationRepository
{
    Task<Notification> SaveAsync(CreateNotificationCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Notification>> GetRecentAsync(int take, string lang, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
