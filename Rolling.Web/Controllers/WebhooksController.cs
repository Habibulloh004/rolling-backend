using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Poster;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
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
    private readonly AppDbContext _dbContext;
    private readonly NotificationService _notificationService;
    private readonly NotificationTokenStore _tokenStore;

    public WebhooksController(
        IWebhookMessageStore messageStore,
        TimeProvider timeProvider,
        ILogger<WebhooksController> logger,
        ICachedPosterService posterCache,
        WebSocketConnectionManager posterUpdateSockets,
        AppDbContext dbContext,
        NotificationService notificationService,
        NotificationTokenStore tokenStore)
    {
        _messageStore = messageStore;
        _timeProvider = timeProvider;
        _logger = logger;
        _posterCache = posterCache;
        _posterUpdateSockets = posterUpdateSockets;
        _dbContext = dbContext;
        _notificationService = notificationService;
        _tokenStore = tokenStore;
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

        if (!TryParseTransactionStatus(payload, out var update))
        {
            return;
        }

        var order = await FindOrderForTransactionAsync(update.TransactionId, cancellationToken);
        if (order is null)
        {
            _logger.LogInformation("No order found for transaction webhook {TransactionId}", update.TransactionId);
            return;
        }

        var didUpdateMetadata = false;
        if (string.IsNullOrWhiteSpace(order.PosterTransactionId))
        {
            order.PosterTransactionId = update.TransactionId;
            didUpdateMetadata = true;
        }

        var mergedStatus = MergeStatus(order.Status, update.Status);
        var statusChanged = mergedStatus != order.Status;
        if (!statusChanged && !didUpdateMetadata)
        {
            return;
        }

        order.Status = mergedStatus;
        order.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        if (mergedStatus == OrderStatus.Delivered && order.ActualDeliveryTime is null)
        {
            order.ActualDeliveryTime = _timeProvider.GetUtcNow().UtcDateTime;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!statusChanged || !ShouldNotifyStatus(mergedStatus))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(order.FcmToken))
        {
            _logger.LogInformation("Order {OrderId} has no FCM token, skipping push.", order.Id);
            return;
        }

        var language = ResolveLanguage(order.FcmToken);
        var payloadMessage = BuildNotificationPayload(mergedStatus, language, order.OrderNumber);
        var data = new Dictionary<string, string>
        {
            ["status"] = ((int)mergedStatus).ToString(),
            ["orderId"] = order.Id,
            ["orderNumber"] = order.OrderNumber,
            ["posterTransactionId"] = order.PosterTransactionId ?? update.TransactionId
        };

        if (!string.IsNullOrWhiteSpace(order.PosterIncomingOrderId))
        {
            data["posterIncomingOrderId"] = order.PosterIncomingOrderId!;
        }

        data["deeplink"] = $"centro://orders/{order.Id}/track";

        await _notificationService.SendToDeviceAsync(
            order.FcmToken!,
            language,
            "orderStatus",
            payloadMessage,
            data,
            cancellationToken);

        _logger.LogInformation("Sent order status push for {OrderId} -> {Status}", order.Id, mergedStatus);
    }

    private async Task<Order?> FindOrderForTransactionAsync(string transactionId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeOrderIdentifier(transactionId);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        var hashPrefixed = $"#{normalized}";

        return await _dbContext.Orders.FirstOrDefaultAsync(order =>
                order.PosterTransactionId == normalized ||
                order.PosterIncomingOrderId == normalized ||
                order.OrderNumber == normalized ||
                order.OrderNumber == hashPrefixed,
            cancellationToken);
    }

    private static bool TryParseTransactionStatus(JsonElement payload, out TransactionStatusUpdate update)
    {
        update = new TransactionStatusUpdate(string.Empty, OrderStatus.Pending, string.Empty);

        if (!TryGetString(payload, "object", out var objectType) ||
            !string.Equals(objectType, "transaction", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetString(payload, "object_id", out var transactionId) ||
            string.IsNullOrWhiteSpace(transactionId))
        {
            return false;
        }

        if (!TryGetObject(payload, "data", out var dataObject) &&
            !TryGetEmbeddedObject(payload, "data", out dataObject))
        {
            return false;
        }

        if (!TryGetObject(dataObject, "transactions_history", out var history) &&
            !TryGetEmbeddedObject(dataObject, "transactions_history", out history))
        {
            return false;
        }

        if (!TryGetString(history, "type_history", out var typeHistory))
        {
            return false;
        }

        if (!TryMapStatus(typeHistory, history, out var status))
        {
            return false;
        }

        update = new TransactionStatusUpdate(transactionId, status, typeHistory);
        return true;
    }

    private static bool TryMapStatus(string typeHistory, JsonElement history, out OrderStatus status)
    {
        status = default;

        switch (typeHistory)
        {
            case "changeprocessingstatus":
                if (!TryGetInt(history, "value", out var processingCode))
                {
                    return false;
                }
                return TryMapProcessingStatus(processingCode, out status);

            case "changeorderstatus":
                if (!TryGetInt(history, "value", out var orderStatusCode))
                {
                    return false;
                }
                return TryMapOrderStatusCode(orderStatusCode, out status);

            case "settable":
                // settable event is sent when order is accepted in Poster
                status = OrderStatus.Accepted;
                return true;

            case "close":
                status = MapCloseStatus(history);
                return true;

            case "open":
                status = OrderStatus.Pending;
                return true;
        }

        return false;
    }

    private static bool TryMapProcessingStatus(int code, out OrderStatus status)
    {
        switch (code)
        {
            case 10:
                status = OrderStatus.Pending;
                return true;
            case 20:
                status = OrderStatus.Accepted;
                return true;
            case 30:
                status = OrderStatus.Preparing;
                return true;
            case 40:
                status = OrderStatus.OnTheWay;
                return true;
            case 50:
                status = OrderStatus.Delivered;
                return true;
            case 60:
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static bool TryMapOrderStatusCode(int code, out OrderStatus status)
    {
        // changeorderstatus webhook event codes (different from incoming order status API)
        // These codes are specific to the webhook event, not the incomingOrder.status field
        switch (code)
        {
            case 1:
                status = OrderStatus.Accepted;
                return true;
            case 4:
                status = OrderStatus.Cancelled;
                return true;
            default:
                // Unknown code - don't change status
                status = default;
                return false;
        }
    }

    private static OrderStatus MapCloseStatus(JsonElement history)
    {
        if (TryGetObject(history, "value_text", out var valueText) ||
            TryGetEmbeddedObject(history, "value_text", out valueText))
        {
            if (TryGetString(valueText, "reason", out var reason))
            {
                return reason switch
                {
                    "1" => OrderStatus.Cancelled,
                    "2" => OrderStatus.Cancelled,
                    "4" => OrderStatus.Cancelled,
                    "3" => OrderStatus.Delivered,
                    _ => OrderStatus.Delivered
                };
            }
        }

        return OrderStatus.Delivered;
    }

    private static OrderStatus MergeStatus(OrderStatus current, OrderStatus incoming)
    {
        if (incoming == OrderStatus.Cancelled)
        {
            return OrderStatus.Cancelled;
        }

        return StatusRank(incoming) >= StatusRank(current) ? incoming : current;
    }

    private static int StatusRank(OrderStatus status) =>
        status switch
        {
            OrderStatus.AwaitingPayment => -1,
            OrderStatus.Pending => 0,
            OrderStatus.Accepted => 1,
            OrderStatus.Preparing => 2,
            OrderStatus.OnTheWay => 3,
            OrderStatus.Delivered => 4,
            OrderStatus.Cancelled => 5,
            _ => 0
        };

    private static bool ShouldNotifyStatus(OrderStatus status) =>
        status is OrderStatus.Accepted
            or OrderStatus.Preparing
            or OrderStatus.OnTheWay
            or OrderStatus.Delivered
            or OrderStatus.Cancelled;

    private string ResolveLanguage(string token)
    {
        if (_tokenStore.TryGet(token, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.Language) &&
            NotificationService.IsLanguageSupported(entry.Language))
        {
            return entry.Language!;
        }

        return "en";
    }

    private static NotificationPayload BuildNotificationPayload(
        OrderStatus status,
        string language,
        string? orderNumber)
    {
        var title = BuildOrderTitle(orderNumber);
        var body = BuildStatusMessage(status, language);
        return new NotificationPayload(title, body);
    }

    private static string BuildOrderTitle(string? orderNumber)
    {
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            return "Rolling Sushi";
        }

        return orderNumber.StartsWith("#", StringComparison.Ordinal)
            ? orderNumber
            : $"#{orderNumber}";
    }

    private static string BuildStatusMessage(OrderStatus status, string language)
    {
        return (status, language) switch
        {
            (OrderStatus.Accepted, "ru") => "Rolling Sushi подтвердил заказ",
            (OrderStatus.Accepted, "uz") => "Rolling Sushi buyurtmani tasdiqladi",
            (OrderStatus.Accepted, _) => "Rolling Sushi confirmed your order",

            (OrderStatus.Preparing, "ru") => "Шефы крутят ваши суши",
            (OrderStatus.Preparing, "uz") => "Oshpazlarimiz sushini tayyorlamoqda",
            (OrderStatus.Preparing, _) => "Chefs are rolling your sushi",

            (OrderStatus.OnTheWay, "ru") => "Курьер уже в пути",
            (OrderStatus.OnTheWay, "uz") => "Kuryer yo'lda",
            (OrderStatus.OnTheWay, _) => "Courier is on the way",

            (OrderStatus.Delivered, "ru") => "Заказ доставлен. Приятного аппетита!",
            (OrderStatus.Delivered, "uz") => "Buyurtma yetkazildi. Yoqimli ishtaha!",
            (OrderStatus.Delivered, _) => "Delivered. Enjoy your meal!",

            (OrderStatus.Cancelled, "ru") => "Заказ отменён",
            (OrderStatus.Cancelled, "uz") => "Buyurtma bekor qilindi",
            (OrderStatus.Cancelled, _) => "Order was cancelled",

            _ => "Order updated"
        };
    }

    private static string? NormalizeOrderIdentifier(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        switch (propertyElement.ValueKind)
        {
            case JsonValueKind.String:
                value = propertyElement.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            case JsonValueKind.Number:
                value = propertyElement.GetRawText();
                return !string.IsNullOrWhiteSpace(value);
            default:
                return false;
        }
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Number => propertyElement.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(propertyElement.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind == JsonValueKind.Object)
        {
            value = propertyElement;
            return true;
        }

        return false;
    }

    private static bool TryGetEmbeddedObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private sealed record TransactionStatusUpdate(string TransactionId, OrderStatus Status, string TypeHistory);

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
}
