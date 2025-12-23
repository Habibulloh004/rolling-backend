using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Poster;
using Rolling.Web.Services.Webhooks;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/webhooks")]
public sealed class WebhooksController : ControllerBase
{
    private readonly IWebhookMessageStore _messageStore;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhooksController> _logger;
    private readonly ICachedPosterService _posterCache;

    public WebhooksController(
        IWebhookMessageStore messageStore,
        TimeProvider timeProvider,
        ILogger<WebhooksController> logger,
        ICachedPosterService posterCache)
    {
        _messageStore = messageStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _posterCache = posterCache;
    }

    [HttpPost("messages")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Receive([FromBody] JsonElement payload, CancellationToken cancellationToken = default)
    {
        var message = new WebhookMessage(
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            payload.ValueKind == JsonValueKind.Undefined ? string.Empty : payload.GetRawText());
        _logger.LogInformation("Webhook message received at {Timestamp}: {Payload}", message.ReceivedAt, message.Payload);

        _messageStore.Add(message);
        await HandlePosterCacheInvalidationAsync(payload, cancellationToken);

        return Ok(new { status = 200 });
    }

    [HttpGet("messages")]
    [ProducesResponseType(typeof(IReadOnlyCollection<JsonNode>), StatusCodes.Status200OK)]
    public IActionResult GetMessages()
    {
        var payloads = new List<JsonNode>();

        foreach (var message in _messageStore.GetAll())
        {
            if (string.IsNullOrWhiteSpace(message.Payload))
            {
                continue;
            }

            try
            {
                var node = JsonNode.Parse(message.Payload);
                NormalizeJsonNode(node);
                if (node is not null)
                {
                    payloads.Add(node.DeepClone());
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse webhook payload {Payload}", message.Payload);
            }
        }

        return Ok(payloads);
    }

    private async Task HandlePosterCacheInvalidationAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var context = new PosterCacheInvalidationContext();
        AnalyzePosterPayload(payload, context);

        if (context.ShouldRefreshCategories)
        {
            _logger.LogInformation("Poster categories cache invalidated via webhook payload");
            _ = await _posterCache.RevalidateCategoriesAsync(cancellationToken);
        }

        if (context.ShouldRefreshProducts)
        {
            _logger.LogInformation("Poster products cache invalidated via webhook payload");
            _ = await _posterCache.RevalidateAllProductsAsync(cancellationToken);
        }
    }

    private static void AnalyzePosterPayload(JsonElement payload, PosterCacheInvalidationContext context)
    {
        switch (payload.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var element in payload.EnumerateArray())
                {
                    AnalyzePosterPayload(element, context);
                }
                break;

            case JsonValueKind.Object:
                if (payload.TryGetProperty("object", out var objectProperty) && objectProperty.ValueKind == JsonValueKind.String)
                {
                    var objectType = objectProperty.GetString();
                    if (string.Equals(objectType, "category", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshCategories = true;
                    }
                    else if (string.Equals(objectType, "product", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshProducts = true;
                    }
                }
                break;
        }
    }

    private void NormalizeJsonNode(JsonNode? node)
    {
        if (node is null)
        {
            return;
        }

        switch (node)
        {
            case JsonObject obj:
                var keys = obj.Select(kvp => kvp.Key).ToList();
                foreach (var key in keys)
                {
                    var child = obj[key];
                    if (child is JsonValue value && value.TryGetValue<string>(out var stringValue) && TryParseJsonString(stringValue, out var parsedNode))
                    {
                        obj[key] = parsedNode;
                        NormalizeJsonNode(parsedNode);
                        continue;
                    }

                    NormalizeJsonNode(child);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child is JsonValue value && value.TryGetValue<string>(out var stringValue) && TryParseJsonString(stringValue, out var parsedNode))
                    {
                        array[i] = parsedNode;
                        NormalizeJsonNode(parsedNode);
                        continue;
                    }

                    NormalizeJsonNode(child);
                }
                break;
        }
    }

    private static bool TryParseJsonString(string? value, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!(trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            node = JsonNode.Parse(trimmed);
            return node is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed class PosterCacheInvalidationContext
    {
        public bool ShouldRefreshCategories { get; set; }
        public bool ShouldRefreshProducts { get; set; }
    }
}
