using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Application.Notifications.Commands;
using Rolling.Application.Notifications.Contracts;
using Rolling.Application.Notifications.DTOs;
using Rolling.Infrastructure.Cache;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ICacheRevalidationPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationService _pushService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        ICacheRevalidationPublisher publisher,
        TimeProvider timeProvider,
        NotificationService pushService,
        AppDbContext dbContext,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _pushService = pushService;
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(NotificationsListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] string lang = "en",
        [FromQuery] int take = 20,
        [FromQuery] int skip = 0,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        var validLang = NormalizeLang(lang);
        var size = Math.Clamp(take, 1, 500);

        var query = _dbContext.Notifications.AsNoTracking();

        // Apply search filter (searches in all language titles and bodies)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(n =>
                n.EnTitle.ToLower().Contains(searchLower) ||
                n.EnBody.ToLower().Contains(searchLower) ||
                n.RuTitle.ToLower().Contains(searchLower) ||
                n.RuBody.ToLower().Contains(searchLower) ||
                n.UzTitle.ToLower().Contains(searchLower) ||
                n.UzBody.ToLower().Contains(searchLower));
        }

        // Apply date range filter
        if (dateFrom.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(n => n.CreatedAt >= fromUtc);
        }

        if (dateTo.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(n => n.CreatedAt <= toUtc);
        }

        // Get total count for pagination
        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(size)
            .ToListAsync(cancellationToken);

        var notifications = items
            .Select(entity => new NotificationAdminDto(
                entity.Id,
                entity.EnTitle,
                entity.EnBody,
                entity.RuTitle,
                entity.RuBody,
                entity.UzTitle,
                entity.UzBody,
                entity.CreatedAt))
            .ToList();

        return Ok(new NotificationsListResponse(notifications, totalCount));
    }

    public sealed record NotificationAdminDto(
        int Id,
        string EnTitle,
        string EnBody,
        string RuTitle,
        string RuBody,
        string UzTitle,
        string UzBody,
        DateTimeOffset CreatedAt);

    public sealed record NotificationsListResponse(
        IReadOnlyCollection<NotificationAdminDto> Items,
        int TotalCount);

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var deleted = await _notificationService.DeleteAsync(id, cancellationToken);
        if (!deleted)
            return NotFound();

        try
        {
            await PublishRevalidationAsync("deleted", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast notification revalidation for delete (Id={Id})", id);
        }
        return NoContent();
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
