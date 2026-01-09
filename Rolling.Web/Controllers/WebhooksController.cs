using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Poster;
using Rolling.Web.Realtime;
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
    private readonly WebSocketConnectionManager _posterUpdateSockets;
    private readonly PosterTransactionWebhookHandler _transactionWebhookHandler;

    public WebhooksController(
        IWebhookMessageStore messageStore,
        TimeProvider timeProvider,
        ILogger<WebhooksController> logger,
        ICachedPosterService posterCache,
        WebSocketConnectionManager posterUpdateSockets,
        PosterTransactionWebhookHandler transactionWebhookHandler)
    {
        _messageStore = messageStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _posterCache = posterCache;
        _posterUpdateSockets = posterUpdateSockets;
        _transactionWebhookHandler = transactionWebhookHandler;
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
        await HandleOrderStatusWebhookAsync(payload, cancellationToken);

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

        if (context.ShouldRefreshPromotions)
        {
            _logger.LogInformation("Poster promotions cache invalidated via webhook payload");
            _ = await _posterCache.RevalidatePromotionsAsync(null, cancellationToken);
        }

        if (context.ShouldRefreshClientGroups)
        {
            _logger.LogInformation("Poster client groups cache invalidated via webhook payload");
            _ = await _posterCache.RevalidateClientGroupsAsync(cancellationToken);
        }

        if (context.ShouldRefreshClients)
        {
            if (context.ClientIds.Count > 0)
            {
                foreach (var clientId in context.ClientIds)
                {
                    _logger.LogInformation("Poster client cache invalidated via webhook payload (client_id={ClientId})", clientId);
                    _ = await _posterCache.RevalidateClientAsync(clientId, cancellationToken);
                }
            }
            else
            {
                _logger.LogInformation("Poster client caches invalidated via webhook payload (no client id)");
                _ = await _posterCache.RevalidateClientsAsync(null, cancellationToken);
            }

            await BroadcastClientUpdatesAsync(context, cancellationToken);
        }

        if (context.ShouldRefreshSpots)
        {
            _logger.LogInformation("Poster spots cache invalidated via webhook payload");
            _ = await _posterCache.RevalidateSpotsAsync(cancellationToken);
        }

        if (context.ShouldRefreshEmployees)
        {
            _logger.LogInformation("Poster employees cache invalidated via webhook payload");
            _ = await _posterCache.RevalidateEmployeesAsync(cancellationToken);
        }

        // Broadcast cache invalidation to all connected WebSocket clients
        await BroadcastCacheInvalidationAsync(context, cancellationToken);
    }

    private async Task HandleOrderStatusWebhookAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (payload.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in payload.EnumerateArray())
            {
                await HandleOrderStatusWebhookAsync(item, cancellationToken);
            }
            return;
        }

        if (!PosterTransactionWebhookParser.TryParseFromEnvelope(payload, out var update))
        {
            return;
        }

        await _transactionWebhookHandler.HandleAsync(update, cancellationToken);
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
                    else if (string.Equals(objectType, "product", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(objectType, "dish", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshProducts = true;
                    }
                    else if (string.Equals(objectType, "promotion", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(objectType, "promotion_prize", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshPromotions = true;
                    }
                    else if (string.Equals(objectType, "client", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(objectType, "client_bonus", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(objectType, "client_payed_sum", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshClients = true;
                        if (TryGetObjectId(payload, out var clientId))
                        {
                            context.ClientIds.Add(clientId);
                        }
                    }
                    else if (string.Equals(objectType, "clients_group", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(objectType, "client_group", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshClientGroups = true;
                    }
                    else if (string.Equals(objectType, "spot", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshSpots = true;
                    }
                    else if (string.Equals(objectType, "employee", StringComparison.OrdinalIgnoreCase))
                    {
                        context.ShouldRefreshEmployees = true;
                    }
                }
                break;
        }
    }

    private static bool TryGetObjectId(JsonElement payload, out string objectId)
    {
        objectId = string.Empty;

        if (!payload.TryGetProperty("object_id", out var objectIdProperty))
        {
            return false;
        }

        switch (objectIdProperty.ValueKind)
        {
            case JsonValueKind.Number:
                objectId = objectIdProperty.GetRawText();
                return !string.IsNullOrWhiteSpace(objectId);
            case JsonValueKind.String:
                objectId = objectIdProperty.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(objectId);
            default:
                return false;
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
        public bool ShouldRefreshPromotions { get; set; }
        public bool ShouldRefreshClients { get; set; }
        public bool ShouldRefreshClientGroups { get; set; }
        public bool ShouldRefreshSpots { get; set; }
        public bool ShouldRefreshEmployees { get; set; }
        public HashSet<string> ClientIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task BroadcastClientUpdatesAsync(PosterCacheInvalidationContext context, CancellationToken cancellationToken)
    {
        var clientIds = context.ClientIds.Count > 0 ? context.ClientIds.ToArray() : Array.Empty<string>();
        var payload = JsonSerializer.Serialize(new
        {
            type = "poster_client_updated",
            clientIds,
            refreshAll = clientIds.Length == 0
        });

        await _posterUpdateSockets.BroadcastAsync(payload, cancellationToken);
    }

    private async Task BroadcastCacheInvalidationAsync(PosterCacheInvalidationContext context, CancellationToken cancellationToken)
    {
        var invalidatedTypes = new List<string>();

        if (context.ShouldRefreshCategories)
            invalidatedTypes.Add("categories");
        if (context.ShouldRefreshProducts)
            invalidatedTypes.Add("products");
        if (context.ShouldRefreshPromotions)
            invalidatedTypes.Add("promotions");
        if (context.ShouldRefreshClients)
            invalidatedTypes.Add("clients");
        if (context.ShouldRefreshClientGroups)
            invalidatedTypes.Add("clientGroups");
        if (context.ShouldRefreshSpots)
            invalidatedTypes.Add("spots");
        if (context.ShouldRefreshEmployees)
            invalidatedTypes.Add("employees");

        if (invalidatedTypes.Count == 0)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            type = "cache_invalidated",
            invalidatedTypes,
            clientIds = context.ClientIds.Count > 0 ? context.ClientIds.ToArray() : Array.Empty<string>(),
            timestamp = _timeProvider.GetUtcNow().ToString("O")
        });

        await _posterUpdateSockets.BroadcastAsync(payload, cancellationToken);
        _logger.LogInformation("Broadcasted cache invalidation for types: {Types}", string.Join(", ", invalidatedTypes));
    }
}
