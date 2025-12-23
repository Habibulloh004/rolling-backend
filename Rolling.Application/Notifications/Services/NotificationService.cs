using System.Linq;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Abstractions.Time;
using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.Contracts;
using Rolling.Application.Notifications.DTOs;
using Rolling.Domain.Notifications;

namespace Rolling.Application.Notifications.Services;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly IClock _clock;

    public NotificationService(
        INotificationRepository repository,
        IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    public async Task<NotificationDto> CreateAsync(CreateNotificationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = Notification.Create(command.Channel, command.Title, command.Message, _clock.UtcNow);
        await _repository.SaveAsync(notification, cancellationToken);
        var dto = Map(notification);
        return dto;
    }

    public async Task<IReadOnlyCollection<NotificationDto>> GetRecentAsync(int take, CancellationToken cancellationToken)
    {
        var targetSize = Math.Clamp(take, 1, 500);
        var items = await _repository.GetRecentAsync(targetSize, cancellationToken);
        return items
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(Map)
            .ToList();
    }

    private static NotificationDto Map(Notification notification) =>
        new(notification.Id, notification.Channel, notification.Title, notification.Message, notification.CreatedAt);
}
