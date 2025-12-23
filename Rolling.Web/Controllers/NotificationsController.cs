using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.Contracts;
using Rolling.Application.Notifications.DTOs;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        var notifications = await _notificationService.GetRecentAsync(take, cancellationToken);
        return Ok(notifications);
    }

    [HttpPost]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateNotificationRequest request, CancellationToken cancellationToken = default)
    {
        var command = new CreateNotificationCommand(request.Channel, request.Title, request.Message);
        var notification = await _notificationService.CreateAsync(command, cancellationToken);
        return Created($"/api/notifications/{notification.Id}", notification);
    }

    public sealed record CreateNotificationRequest(
        [Required, MinLength(2)] string Channel,
        [Required, MinLength(3)] string Title,
        [Required, MinLength(3)] string Message);
}
