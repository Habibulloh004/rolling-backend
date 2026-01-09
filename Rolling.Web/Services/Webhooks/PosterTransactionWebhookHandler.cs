using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Services.Webhooks;

public sealed class PosterTransactionWebhookHandler
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationService _notificationService;
    private readonly NotificationTokenStore _tokenStore;
    private readonly ILogger<PosterTransactionWebhookHandler> _logger;

    public PosterTransactionWebhookHandler(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        NotificationService notificationService,
        NotificationTokenStore tokenStore,
        ILogger<PosterTransactionWebhookHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _notificationService = notificationService;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    public async Task HandleAsync(TransactionStatusUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.TransactionId))
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
}
