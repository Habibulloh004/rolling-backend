using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.DTOs;

namespace Rolling.Application.Notifications.Contracts;

public interface INotificationService
{
    Task<NotificationDto> CreateAsync(CreateNotificationCommand command, string lang, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<NotificationDto>> GetRecentAsync(int take, string lang, CancellationToken cancellationToken);
}
