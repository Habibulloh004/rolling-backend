using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Messaging;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Poster;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Orders;

public sealed class OrderProcessor
{
    private readonly PosterService _posterService;
    private readonly TelegramService _telegramService;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly TakeawayOrderTracker _takeawayOrderTracker;
    private readonly NotificationTokenStore _tokenStore;
    private readonly AppDbContext _dbContext;
    private readonly IOrderUpdatesPublisher _orderUpdatesPublisher;
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(
        PosterService posterService,
        TelegramService telegramService,
        ActiveOrderTracker orderTracker,
        TakeawayOrderTracker takeawayOrderTracker,
        NotificationTokenStore tokenStore,
        AppDbContext dbContext,
        IOrderUpdatesPublisher orderUpdatesPublisher,
        ILogger<OrderProcessor> logger)
    {
        _posterService = posterService;
        _telegramService = telegramService;
        _orderTracker = orderTracker;
        _takeawayOrderTracker = takeawayOrderTracker;
        _tokenStore = tokenStore;
        _dbContext = dbContext;
        _orderUpdatesPublisher = orderUpdatesPublisher;
        _logger = logger;
    }

    public async Task<string?> ProcessAsync(PaymentTransaction order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.OrderDetailsJson))
        {
            return null;
        }

        using var orderDetails = JsonDocument.Parse(string.IsNullOrWhiteSpace(order.OrderDetailsJson) ? "{}" : order.OrderDetailsJson);
        if (!TryGetServiceMode(orderDetails.RootElement, out var serviceMode))
        {
            _logger.LogWarning("Unable to resolve service_mode from order payload. Raw payload: {Payload}", order.OrderDetailsJson);
            return null;
        }

        var result = serviceMode switch
        {
            1 or 2 or 3 => await HandleDeliveryOrderAsync(orderDetails, order.Amount, cancellationToken),
            _ => null
        };

        if (result is null)
        {
            return null;
        }

        var ensuredOrder = await PaymentOrderBuilder.EnsurePaidOrderAsync(
            _dbContext,
            order,
            orderDetails.RootElement,
            result.TransactionId,
            result.IncomingOrderId,
            cancellationToken);

        if (ensuredOrder is not null)
        {
            await TrySendPaidOrderTelegramAsync(ensuredOrder, order, cancellationToken);
            TrackOrderForPolling(ensuredOrder);
            await _orderUpdatesPublisher.PublishAsync(
                OrderUpdateEventFactory.Create(ensuredOrder, "updated"),
                cancellationToken);
        }

        return result.TransactionId ?? result.IncomingOrderId;
    }

    private static bool TryGetServiceMode(JsonElement root, out int serviceMode)
    {
        serviceMode = 0;
        if (!root.TryGetProperty("service_mode", out var element) &&
            !root.TryGetProperty("serviceMode", out element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
        {
            serviceMode = numeric;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            serviceMode = parsed;
            return true;
        }

        return false;
    }

    private async Task<PosterOrderResult?> HandleVenueOrderAsync(JsonDocument orderDetails, decimal amount, CancellationToken cancellationToken)
    {
        var root = orderDetails.RootElement;
        var comment = root.TryGetProperty("comment", out var commentElement) ? commentElement.GetString() ?? string.Empty : string.Empty;
        var spotName = root.TryGetProperty("spot_name", out var spotNameElement) ? spotNameElement.GetString() ?? string.Empty : string.Empty;
        var service = root.TryGetProperty("service", out var serviceElement) ? serviceElement.GetString() : null;

        var posterPayload = NormalizePosterPayload(root, amount);
        using var response = await _posterService.CreateIncomingOrderAsync(posterPayload, cancellationToken);
        var result = ExtractPosterIds(response);
        if (result is null)
        {
            return null;
        }

        var totalAmount = service == "waiter" ? amount + (amount * 0.1m) : amount;
        var message = $"""
📦 Новый заказ!
🛒 Название филиал: {spotName}
📞 Телефон: +998771244444
💵 Сумма заказа: {totalAmount} сум
💳 Метод оплаты: Карта (Оплачено)
🛍 Тип заказа: Заведения
✏️ Комментарий: {comment}
""";

        await _telegramService.SendMessageAsync(message, cancellationToken);
        return result;
    }

    private async Task<PosterOrderResult?> HandleDeliveryOrderAsync(JsonDocument orderDetails, decimal amount, CancellationToken cancellationToken)
    {
        var posterPayload = NormalizePosterPayload(orderDetails.RootElement, amount);
        using var response = await _posterService.CreateIncomingOrderAsync(posterPayload, cancellationToken);
        return ExtractPosterIds(response);
    }

    private static string? GetStringOrNumber(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }

    private static PosterOrderResult? ExtractPosterIds(JsonDocument? response)
    {
        if (response is null)
        {
            return null;
        }

        var transactionId = response.RootElement.TryGetProperty("response", out var resp) &&
                            resp.TryGetProperty("transaction_id", out var txElement)
            ? GetStringOrNumber(txElement)
            : null;

        var incomingOrderId = response.RootElement.TryGetProperty("response", out resp) &&
                              resp.TryGetProperty("incoming_order_id", out var incomingElement)
            ? GetStringOrNumber(incomingElement)
            : null;

        if (string.IsNullOrWhiteSpace(transactionId) && string.IsNullOrWhiteSpace(incomingOrderId))
        {
            return null;
        }

        return new PosterOrderResult(transactionId, incomingOrderId);
    }

    private sealed record PosterOrderResult(string? TransactionId, string? IncomingOrderId);

    private async Task TrySendPaidOrderTelegramAsync(
        Order order,
        PaymentTransaction transaction,
        CancellationToken cancellationToken)
    {
        var provider = transaction.Provider?.Trim().ToLowerInvariant();
        if (provider is not ("payme" or "click"))
        {
            return;
        }

        await EnsureOrderCountForTelegramAsync(order, cancellationToken);

        var paymentDescription = TelegramOrderMessageBuilder.BuildPaymentDescription(order, provider);
        var orderSummary = TelegramOrderMessageBuilder.BuildOrderSummary(order, paymentDescription);
        var context = await TelegramOrderMessageBuilder.CreateContextAsync(
            _dbContext,
            order,
            orderSummary,
            paymentDescription,
            cancellationToken);
        var message = TelegramOrderMessageBuilder.BuildNewOrderMessage(context);

        try
        {
            await _telegramService.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send Telegram new order message: OrderId={OrderId}, Provider={Provider}",
                order.Id,
                provider);
        }
    }

    private async Task EnsureOrderCountForTelegramAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.UserOrderCount is >= 0)
        {
            return;
        }

        var resolvedOrderCount = await TryResolvePosterClientOrderCountAsync(order, cancellationToken);
        if (!resolvedOrderCount.HasValue)
        {
            return;
        }

        order.UserOrderCount = resolvedOrderCount.Value;
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to persist resolved order count before Telegram message: OrderId={OrderId}",
                order.Id);
        }
    }

    private async Task<int?> TryResolvePosterClientOrderCountAsync(Order order, CancellationToken cancellationToken)
    {
        var clientId = order.UserId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return null;
        }

        try
        {
            using var clientDocument = await _posterService.GetClientAsync(clientId, cancellationToken);
            if (clientDocument is null || !TryExtractClient(clientDocument.RootElement, out var client))
            {
                return null;
            }

            var comment = client.TryGetProperty("comment", out var commentElement)
                ? GetStringOrNumber(commentElement)
                : null;

            return TryParseOrderCountFromComment(comment);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to resolve client order count from Poster: OrderId={OrderId}, ClientId={ClientId}",
                order.Id,
                clientId);
            return null;
        }
    }

    private static bool TryExtractClient(JsonElement root, out JsonElement client)
    {
        client = default;

        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("response", out var response))
        {
            if (response.ValueKind == JsonValueKind.Object)
            {
                client = response;
                return true;
            }

            if (response.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in response.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        client = item;
                        return true;
                    }
                }
            }
        }

        if (root.TryGetProperty("client_id", out _))
        {
            client = root;
            return true;
        }

        return false;
    }

    private static int? TryParseOrderCountFromComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return null;
        }

        var trimmed = comment.Trim();
        if (TryParseOrderCountFromJson(trimmed, out var parsed))
        {
            return parsed;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                var unescaped = JsonSerializer.Deserialize<string>(trimmed);
                if (TryParseOrderCountFromJson(unescaped, out parsed))
                {
                    return parsed;
                }
            }
            catch (JsonException)
            {
                // Ignore and continue.
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var jsonObject = trimmed[start..(end + 1)];
            if (TryParseOrderCountFromJson(jsonObject, out parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool TryParseOrderCountFromJson(string? json, out int orderCount)
    {
        orderCount = 0;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("length", out var lengthElement))
            {
                return false;
            }

            orderCount = lengthElement.ValueKind switch
            {
                JsonValueKind.Number when lengthElement.TryGetInt32(out var numeric) => numeric,
                JsonValueKind.String when int.TryParse(lengthElement.GetString()?.Trim(), out var fromString) => fromString,
                _ => 0
            };

            if (orderCount < 0)
            {
                orderCount = 0;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonElement NormalizePosterPayload(JsonElement root, decimal referenceTotal)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root;
        }

        if (!ShouldConvertToMinorUnits(root, referenceTotal))
        {
            return root;
        }

        var node = JsonNode.Parse(root.GetRawText()) as JsonObject;
        if (node is null)
        {
            return root;
        }

        NormalizeMoneyField(node, "delivery_price");

        if (node.TryGetPropertyValue("products", out var productsNode) && productsNode is JsonArray products)
        {
            foreach (var productNode in products)
            {
                if (productNode is not JsonObject productObject)
                {
                    continue;
                }

                NormalizeMoneyField(productObject, "price");
                NormalizeMoneyField(productObject, "price_override");
            }
        }

        return JsonSerializer.SerializeToElement(node);
    }

    private static void NormalizeMoneyField(JsonObject obj, string field)
    {
        if (!obj.TryGetPropertyValue(field, out var valueNode) || valueNode is null)
        {
            return;
        }

        if (!TryGetDecimal(valueNode, out var value))
        {
            return;
        }

        if (value <= 0m)
        {
            return;
        }

        var converted = (long)Math.Round(value * 100m);
        obj[field] = JsonValue.Create(converted);
    }

    private static bool TryGetDecimal(JsonNode node, out decimal value)
    {
        value = 0m;
        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue(out decimal numeric))
            {
                value = numeric;
                return true;
            }

            if (jsonValue.TryGetValue(out string? text) &&
                !string.IsNullOrWhiteSpace(text) &&
                decimal.TryParse(text, out var parsed))
            {
                value = parsed;
                return true;
            }
        }

        return false;
    }

    private static bool ShouldConvertToMinorUnits(JsonElement root, decimal referenceTotal)
    {
        if (referenceTotal <= 0m)
        {
            return true;
        }

        var total = GetDecimal(root, "total");
        if (total.HasValue && total.Value > referenceTotal * 3m)
        {
            return false;
        }

        return true;
    }

    private static decimal? GetDecimal(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numeric))
        {
            return numeric;
        }

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private void TrackOrderForPolling(Order order)
    {
        UpdateTakeawayTracker(order);

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return;
        }

        var fcmToken = order.FcmToken;
        var language = ResolveLanguage(fcmToken);

        _orderTracker.TrackOrder(new TrackedOrder
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PosterIncomingOrderId = order.PosterIncomingOrderId,
            PosterTransactionId = order.PosterTransactionId,
            FcmToken = fcmToken,
            Phone = order.Phone,
            CurrentStatus = order.Status,
            ServiceMode = order.ServiceMode,
            Language = language
        });
    }

    private void UpdateTakeawayTracker(Order order)
    {
        if (order.ServiceMode != 2)
        {
            return;
        }

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            _takeawayOrderTracker.UntrackOrder(order.Id);
            return;
        }

        _takeawayOrderTracker.TrackOrder(order);
    }

    private string ResolveLanguage(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token) &&
            _tokenStore.TryGet(token, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.Language) &&
            NotificationService.IsLanguageSupported(entry.Language))
        {
            return entry.Language!.Trim().ToLowerInvariant();
        }

        return "en";
    }
}
