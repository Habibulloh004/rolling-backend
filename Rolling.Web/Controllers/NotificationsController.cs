using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.Contracts;
using Rolling.Application.Notifications.DTOs;
using Rolling.Infrastructure.Cache;
using Rolling.Infrastructure.Notifications;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ICacheRevalidationPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationService _pushService;

    public NotificationsController(
        INotificationService notificationService,
        ICacheRevalidationPublisher publisher,
        TimeProvider timeProvider,
        NotificationService pushService)
    {
        _notificationService = notificationService;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _pushService = pushService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string lang = "en",
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var validLang = NormalizeLang(lang);
        var notifications = await _notificationService.GetRecentAsync(take, validLang, cancellationToken);
        return Ok(notifications);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreateNotificationResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateNotificationRequest request,
        [FromQuery] string lang = "en",
        CancellationToken cancellationToken = default)
    {
        var command = new CreateNotificationCommand(
            request.EnTitle,
            request.EnBody,
            request.RuTitle,
            request.RuBody,
            request.UzTitle,
            request.UzBody);

        var validLang = NormalizeLang(lang);
        var notification = await _notificationService.CreateAsync(command, validLang, cancellationToken);
        await PublishRevalidationAsync("created", cancellationToken);

        // Send push notifications if requested
        List<PushResult>? pushResults = null;
        if (request.SendPush)
        {
            pushResults = await SendPushToAllLanguagesAsync(request, cancellationToken);
        }

        var response = new CreateNotificationResponse(notification, pushResults);
        return Created($"/api/notifications/{notification.Id}", response);
    }

    private async Task<List<PushResult>> SendPushToAllLanguagesAsync(
        CreateNotificationRequest request,
        CancellationToken cancellationToken)
    {
        var languages = new[] { "en", "ru", "uz" };
        var results = new List<PushResult>();

        foreach (var language in languages)
        {
            var (title, body) = language switch
            {
                "ru" => (
                    string.IsNullOrEmpty(request.RuTitle) ? request.EnTitle : request.RuTitle,
                    string.IsNullOrEmpty(request.RuBody) ? request.EnBody : request.RuBody
                ),
                "uz" => (
                    string.IsNullOrEmpty(request.UzTitle) ? request.EnTitle : request.UzTitle,
                    string.IsNullOrEmpty(request.UzBody) ? request.EnBody : request.UzBody
                ),
                _ => (request.EnTitle, request.EnBody)
            };

            var topic = $"all_users_{language}";
            try
            {
                await _pushService.SendToTopicAsync(
                    topic,
                    language,
                    "newNotification",
                    new NotificationPayload(title, body),
                    null,
                    cancellationToken);

                results.Add(new PushResult(language, topic, true, null));
            }
            catch (Exception ex)
            {
                results.Add(new PushResult(language, topic, false, ex.Message));
            }
        }

        return results;
    }

    public sealed record CreateNotificationRequest(
        [Required, MinLength(1)] string EnTitle,
        [Required, MinLength(1)] string EnBody,
        string RuTitle = "",
        string RuBody = "",
        string UzTitle = "",
        string UzBody = "",
        bool SendPush = false);

    public sealed record CreateNotificationResponse(
        NotificationDto Notification,
        List<PushResult>? PushResults);

    public sealed record PushResult(
        string Language,
        string Topic,
        bool Success,
        string? Error);

    private static string NormalizeLang(string lang)
    {
        var normalized = lang?.ToLowerInvariant().Trim() ?? "en";
        return normalized switch
        {
            "ru" => "ru",
            "uz" => "uz",
            _ => "en"
        };
    }

    private Task PublishRevalidationAsync(string changeType, CancellationToken cancellationToken)
    {
        var updatedAt = _timeProvider.GetUtcNow();
        var payload = new CacheRevalidationEvent(
            CacheResources.Notifications,
            changeType,
            updatedAt.ToUnixTimeMilliseconds().ToString(),
            updatedAt);

        return _publisher.PublishAsync(payload, cancellationToken);
    }
}
