using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.Contracts;
using Rolling.Application.Notifications.DTOs;
using Rolling.Domain.Notifications;

namespace Rolling.Application.Notifications.Services;

public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;

    public NotificationService(INotificationRepository repository)
    {
        _repository = repository;
    }

    public async Task<NotificationDto> CreateAsync(CreateNotificationCommand command, string lang, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = await _repository.SaveAsync(command, cancellationToken);
        return Map(notification);
    }

    public async Task<IReadOnlyCollection<NotificationDto>> GetRecentAsync(int take, string lang, CancellationToken cancellationToken)
    {
        var targetSize = Math.Clamp(take, 1, 500);
        var items = await _repository.GetRecentAsync(targetSize, lang, cancellationToken);
        return items
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(Map)
            .ToList();
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        return await _repository.DeleteAsync(id, cancellationToken);
    }

    private static NotificationDto Map(Notification notification) =>
        new(notification.Id, notification.Title, notification.Message, notification.CreatedAt);
}
