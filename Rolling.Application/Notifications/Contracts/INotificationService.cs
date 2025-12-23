using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.DTOs;

namespace Rolling.Application.Notifications.Contracts;

public interface INotificationService
{
    Task<NotificationDto> CreateAsync(CreateNotificationCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NotificationDto>> GetRecentAsync(int take, CancellationToken cancellationToken);
}
