using System.Text.Json;
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
    private readonly NotificationTokenStore _tokenStore;
    private readonly AppDbContext _dbContext;
    private readonly IOrderUpdatesPublisher _orderUpdatesPublisher;
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(
        PosterService posterService,
        TelegramService telegramService,
        ActiveOrderTracker orderTracker,
        NotificationTokenStore tokenStore,
        AppDbContext dbContext,
        IOrderUpdatesPublisher orderUpdatesPublisher,
        ILogger<OrderProcessor> logger)
    {
        _posterService = posterService;
        _telegramService = telegramService;
        _orderTracker = orderTracker;
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
            1 => await HandleVenueOrderAsync(orderDetails, order.Amount, cancellationToken),
            2 or 3 => await HandleDeliveryOrderAsync(orderDetails, cancellationToken),
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
        if (!root.TryGetProperty("service_mode", out var element))
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

        using var response = await _posterService.CreateIncomingOrderAsync(root, cancellationToken);
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

    private async Task<PosterOrderResult?> HandleDeliveryOrderAsync(JsonDocument orderDetails, CancellationToken cancellationToken)
    {
        using var response = await _posterService.CreateIncomingOrderAsync(orderDetails.RootElement, cancellationToken);
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

    private void TrackOrderForPolling(Order order)
    {
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
            Language = language
        });
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
