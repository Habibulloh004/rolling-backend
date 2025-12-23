using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Notifications;
using Rolling.Web.Models.Notifications;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class PushNotificationsController : ControllerBase
{
    private readonly NotificationService _notificationService;
    private readonly NotificationTokenStore _tokenStore;
    private readonly ILogger<PushNotificationsController> _logger;

    public PushNotificationsController(
        NotificationService notificationService,
        NotificationTokenStore tokenStore,
        ILogger<PushNotificationsController> logger)
    {
        _notificationService = notificationService;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    [HttpPost("notify")]
    public async Task<IActionResult> NotifyAsync([FromBody] NotifyRequest request, CancellationToken cancellationToken)
    {
        if (!NotificationService.IsLanguageSupported(request.Language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        await _notificationService.SendToTopicAsync(
            request.Topic,
            request.Language,
            request.MessageType,
            null,
            null,
            cancellationToken);

        return Ok("ok");
    }

    [HttpGet("tokens-get")]
    public IActionResult GetTokens() =>
        Ok(_tokenStore.GetAll().Select(pair => new
        {
            token = pair.Key,
            pair.Value.Language,
            pair.Value.UserId,
            pair.Value.RegisteredAt,
            pair.Value.LastUsed,
            pair.Value.IsValid
        }));

    [HttpGet("health")]
    public IActionResult Health()
    {
        var stats = _tokenStore.GetStats();
        return Ok(new
        {
            status = "OK",
            timestamp = DateTimeOffset.UtcNow,
            supportedLanguages = new[] { "en", "uz", "ru" },
            totalTokens = stats.TotalTokens,
            stats.ValidTokens
        });
    }

    [HttpPost("test-direct")]
    public async Task<IActionResult> TestDirectAsync([FromBody] TestDirectRequest request, CancellationToken cancellationToken)
    {
        await _notificationService.SendToDeviceAsync(
            request.Token,
            "en",
            "test",
            new NotificationPayload("Test Direct", "This is a direct test message"),
            new Dictionary<string, string>
            {
                ["messageType"] = "test",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
            },
            cancellationToken);

        return Ok(new { success = true });
    }

    [HttpPost("tokens/register")]
    public async Task<IActionResult> RegisterTokenAsync([FromBody] TokenRegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceToken))
        {
            return BadRequest(new { error = "Device token is required" });
        }

        if (!NotificationService.IsLanguageSupported(request.Language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        var isValid = await _notificationService.ValidateTokenAsync(request.DeviceToken, cancellationToken);
        _tokenStore.AddOrUpdate(request.DeviceToken, new NotificationTokenEntry(
            request.Language,
            request.UserId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            isValid));

        await _notificationService.SubscribeAsync(request.DeviceToken, request.Language, cancellationToken);

        return Ok(new { success = true, topic = $"all_users_{request.Language}", tokenValid = isValid });
    }

    [HttpPut("tokens/language")]
    public async Task<IActionResult> UpdateTokenLanguageAsync([FromBody] TokenLanguageUpdateRequest request, CancellationToken cancellationToken)
    {
        if (!_tokenStore.TryGet(request.DeviceToken, out var entry))
        {
            return NotFound(new { error = "Token not found" });
        }

        if (!NotificationService.IsLanguageSupported(request.Language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        // Unsubscribe from the old language-specific topic before subscribing to the new one
        var oldLanguage = entry.Language;
        _tokenStore.AddOrUpdate(request.DeviceToken, entry with { Language = request.Language });
        await _notificationService.UnsubscribeAsync(request.DeviceToken, oldLanguage, cancellationToken);
        await _notificationService.SubscribeAsync(request.DeviceToken, request.Language, cancellationToken);
        return Ok(new { success = true });
    }

    [HttpDelete("tokens/unregister")]
    public async Task<IActionResult> UnregisterTokenAsync([FromBody] UnsubscribeRequest request, CancellationToken cancellationToken)
    {
        if (_tokenStore.TryGet(request.DeviceToken, out var entry))
        {
            // Use the stored language to remove language-specific subscriptions
            await _notificationService.UnsubscribeAsync(request.DeviceToken, entry.Language, cancellationToken);
        }

        _tokenStore.Remove(request.DeviceToken);
        return Ok(new { success = true });
    }

    [HttpGet("tokens/{token}")]
    public IActionResult GetToken(string token)
    {
        return _tokenStore.TryGet(token, out var entry)
            ? Ok(entry)
            : NotFound(new { error = "Token not found" });
    }

    [HttpPost("admin/clean-tokens")]
    public IActionResult CleanTokens()
    {
        _tokenStore.Cleanup(TimeSpan.FromDays(30));
        return Ok(new { success = true });
    }

    [HttpPost("send/topic/{language}")]
    public async Task<IActionResult> SendToTopicAsync(string language, [FromBody] SendTopicRequest request, CancellationToken cancellationToken)
    {
        if (!NotificationService.IsLanguageSupported(language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        var topic = $"all_users_{language}";
        var custom = request.CustomTitle is not null && request.CustomBody is not null
            ? new NotificationPayload(request.CustomTitle, request.CustomBody)
            : null;

        await _notificationService.SendToTopicAsync(topic, language, request.MessageType, custom, request.Data, cancellationToken);
        return Ok(new { success = true, topic });
    }

    [HttpPost("send/all-languages")]
    public async Task<IActionResult> SendAllLanguagesAsync([FromBody] SendAllLanguagesRequest request, CancellationToken cancellationToken)
    {
        var languages = new[] { "en", "ru", "uz" };
        var responses = new List<object>();

        foreach (var language in languages)
        {
            NotificationPayload? custom = null;
            if (request.CustomMessages is not null && request.CustomMessages.TryGetValue(language, out var payload))
            {
                custom = new NotificationPayload(payload.Title, payload.Body);
            }

            var topic = $"all_users_{language}";
            await _notificationService.SendToTopicAsync(topic, language, request.MessageType, custom, request.Data, cancellationToken);
            responses.Add(new { language, topic });
        }

        return Ok(new { success = true, results = responses });
    }

    [HttpPost("send/device")]
    public async Task<IActionResult> SendDeviceAsync([FromBody] SendDeviceRequest request, CancellationToken cancellationToken)
    {
        if (!NotificationService.IsLanguageSupported(request.Language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        var custom = request.CustomTitle is not null && request.CustomBody is not null
            ? new NotificationPayload(request.CustomTitle, request.CustomBody)
            : null;

        await _notificationService.SendToDeviceAsync(request.DeviceToken, request.Language, request.MessageType, custom, request.Data, cancellationToken);
        return Ok(new { success = true });
    }

    [HttpPost("send/devices")]
    public async Task<IActionResult> SendDevicesAsync([FromBody] SendDevicesRequest request, CancellationToken cancellationToken)
    {
        if (request.DeviceTokens is null || request.DeviceTokens.Count == 0)
        {
            return BadRequest(new { error = "Device tokens array is required" });
        }

        if (!NotificationService.IsLanguageSupported(request.Language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        var custom = request.CustomTitle is not null && request.CustomBody is not null
            ? new NotificationPayload(request.CustomTitle, request.CustomBody)
            : null;

        await _notificationService.SendToDevicesAsync(request.DeviceTokens, request.Language, request.MessageType, custom, request.Data, cancellationToken);
        return Ok(new { success = true, count = request.DeviceTokens.Count });
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> SubscribeAsync([FromBody] SubscribeRequest request, CancellationToken cancellationToken)
    {
        if (!NotificationService.IsLanguageSupported(request.Language))
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        var isValid = await _notificationService.ValidateTokenAsync(request.DeviceToken, cancellationToken);
        _tokenStore.AddOrUpdate(request.DeviceToken, new NotificationTokenEntry(
            request.Language,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            isValid));

        await _notificationService.SubscribeAsync(request.DeviceToken, request.Language, cancellationToken);
        return Ok(new { success = true, topic = $"all_users_{request.Language}", tokenValid = isValid });
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> UnsubscribeAsync([FromBody] UnsubscribeRequest request, CancellationToken cancellationToken)
    {
        _tokenStore.Remove(request.DeviceToken);
        await _notificationService.UnsubscribeAsync(request.DeviceToken, request.Language, cancellationToken);
        return Ok(new { success = true });
    }

    [HttpGet("messages")]
    public IActionResult GetMessages() => Ok(new
    {
        supportedLanguages = new[] { "en", "uz", "ru" },
        messageTypes = new[] { "body", "orderReady", "newPromotion", "welcomeMessage" },
        messages = _notificationService.GetMessages()
    });

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = _tokenStore.GetStats();
        return Ok(new
        {
            success = true,
            stats = new
            {
                stats.TotalTokens,
                stats.ValidTokens,
                stats.InvalidTokens,
                stats.LanguageBreakdown,
                stats.RegisteredToday
            },
            timestamp = DateTimeOffset.UtcNow
        });
    }
}
