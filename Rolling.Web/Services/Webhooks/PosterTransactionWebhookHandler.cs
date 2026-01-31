using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Application.Abstractions.Realtime;

namespace Rolling.Web.Services.Webhooks;

public sealed class PosterTransactionWebhookHandler
{
    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly NotificationService _notificationService;
    private readonly NotificationTokenStore _tokenStore;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly IOrderUpdatesPublisher _orderUpdatesPublisher;
    private readonly ILogger<PosterTransactionWebhookHandler> _logger;

    public PosterTransactionWebhookHandler(
        AppDbContext dbContext,
        TimeProvider timeProvider,
        NotificationService notificationService,
        NotificationTokenStore tokenStore,
        ActiveOrderTracker orderTracker,
        IOrderUpdatesPublisher orderUpdatesPublisher,
        ILogger<PosterTransactionWebhookHandler> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _notificationService = notificationService;
        _tokenStore = tokenStore;
        _orderTracker = orderTracker;
        _orderUpdatesPublisher = orderUpdatesPublisher;
        _logger = logger;
    }

    public async Task HandleAsync(TransactionStatusUpdate update, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing transaction webhook: TransactionId={TransactionId}, IncomingStatus={Status}",
            update.TransactionId, update.Status);

        if (string.IsNullOrWhiteSpace(update.TransactionId))
        {
            _logger.LogWarning("Transaction webhook received with empty TransactionId, skipping");
            return;
        }

        var order = await FindOrderForTransactionAsync(update.TransactionId, cancellationToken);
        if (order is null)
        {
            _logger.LogInformation(
                "No order found for transaction webhook: TransactionId={TransactionId}",
                update.TransactionId);
            return;
        }

        _logger.LogDebug(
            "Order found for webhook: OrderId={OrderId}, OrderNumber={OrderNumber}, CurrentStatus={CurrentStatus}",
            order.Id, order.OrderNumber, order.Status);

        var didUpdateMetadata = false;
        if (string.IsNullOrWhiteSpace(order.PosterTransactionId))
        {
            order.PosterTransactionId = update.TransactionId;
            didUpdateMetadata = true;
            _logger.LogDebug(
                "Updated PosterTransactionId for order {OrderId}: {PosterTransactionId}",
                order.Id, update.TransactionId);
        }

        var mergedStatus = MergeStatus(order.Status, update.Status);
        var statusChanged = mergedStatus != order.Status;

        _logger.LogDebug(
            "Status merge result: OrderId={OrderId}, OldStatus={OldStatus}, IncomingStatus={IncomingStatus}, MergedStatus={MergedStatus}, Changed={Changed}",
            order.Id, order.Status, update.Status, mergedStatus, statusChanged);

        if (!statusChanged && !didUpdateMetadata)
        {
            _logger.LogDebug("No status change or metadata update for order {OrderId}, skipping", order.Id);
            return;
        }

        order.Status = mergedStatus;
        order.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        if (mergedStatus == OrderStatus.Delivered && order.ActualDeliveryTime is null)
        {
            order.ActualDeliveryTime = _timeProvider.GetUtcNow().UtcDateTime;
            _logger.LogDebug("Set ActualDeliveryTime for order {OrderId}", order.Id);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Order status updated in database: OrderId={OrderId}, OrderNumber={OrderNumber}, NewStatus={Status}",
            order.Id, order.OrderNumber, mergedStatus);

        await _orderUpdatesPublisher.PublishAsync(
            OrderUpdateEventFactory.Create(order, "updated"),
            cancellationToken);

        UpdateTrackedOrder(order, mergedStatus);

        if (!statusChanged || !ShouldNotifyStatus(mergedStatus))
        {
            _logger.LogDebug(
                "Push notification not required: OrderId={OrderId}, StatusChanged={StatusChanged}, ShouldNotify={ShouldNotify}",
                order.Id, statusChanged, ShouldNotifyStatus(mergedStatus));
            return;
        }

        if (string.IsNullOrWhiteSpace(order.FcmToken))
        {
            _logger.LogWarning(
                "Order has no FCM token, push notification skipped: OrderId={OrderId}, OrderNumber={OrderNumber}, Status={Status}",
                order.Id, order.OrderNumber, mergedStatus);
            return;
        }

        var tokenPreview = order.FcmToken.Length > 20
            ? $"{order.FcmToken[..10]}...{order.FcmToken[^10..]}"
            : order.FcmToken;

        _logger.LogInformation(
            "Preparing push notification for webhook: OrderId={OrderId}, Status={Status}, Token={TokenPreview}",
            order.Id, mergedStatus, tokenPreview);

        var language = ResolveLanguage(order.FcmToken);
        var payloadMessage = BuildNotificationPayload(mergedStatus, language, order.OrderNumber);

        // Use string status for iOS app compatibility (matches fromBackendValue parsing)
        var statusString = mergedStatus switch
        {
            OrderStatus.AwaitingPayment => "awaitingPayment",
            OrderStatus.Pending => "pending",
            OrderStatus.Accepted => "accepted",
            OrderStatus.Preparing => "preparing",
            OrderStatus.OnTheWay => "onTheWay",
            OrderStatus.Delivered => "delivered",
            OrderStatus.Cancelled => "cancelled",
            _ => "pending"
        };

        var data = new Dictionary<string, string>
        {
            ["type"] = "orderStatusUpdate",
            ["status"] = statusString,
            ["orderId"] = order.Id,
            ["order_id"] = order.Id,
            ["orderNumber"] = order.OrderNumber,
            ["order_number"] = order.OrderNumber,
            ["posterTransactionId"] = order.PosterTransactionId ?? update.TransactionId,
            ["poster_transaction_id"] = order.PosterTransactionId ?? update.TransactionId,
            ["transaction_id"] = order.PosterTransactionId ?? update.TransactionId
        };

        if (!string.IsNullOrWhiteSpace(order.PosterIncomingOrderId))
        {
            data["posterIncomingOrderId"] = order.PosterIncomingOrderId!;
            data["poster_incoming_order_id"] = order.PosterIncomingOrderId!;
            data["incoming_order_id"] = order.PosterIncomingOrderId!;
        }

        data["deeplink"] = $"centro://orders/{order.Id}/track";

        try
        {
            await _notificationService.SendToDeviceAsync(
                order.FcmToken!,
                language,
                "orderStatus",
                payloadMessage,
                data,
                cancellationToken);

            _logger.LogInformation(
                "Push notification sent via webhook: OrderId={OrderId}, OrderNumber={OrderNumber}, Status={Status}",
                order.Id, order.OrderNumber, mergedStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send push notification via webhook: OrderId={OrderId}, OrderNumber={OrderNumber}, Status={Status}",
                order.Id, order.OrderNumber, mergedStatus);
            // Don't rethrow - webhook should still return success to Poster
        }
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
        if (current is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return current;
        }

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

    private void UpdateTrackedOrder(Order order, OrderStatus status)
    {
        if (_orderTracker.IsTracked(order.Id))
        {
            _orderTracker.UpdateOrderStatus(order.Id, status);
            if (IsTerminalStatus(status))
            {
                _orderTracker.UntrackOrder(order.Id);
            }
            return;
        }

        if (IsTerminalStatus(status))
        {
            return;
        }

        _orderTracker.TrackOrder(new TrackedOrder
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            PosterIncomingOrderId = order.PosterIncomingOrderId,
            PosterTransactionId = order.PosterTransactionId,
            FcmToken = order.FcmToken,
            Phone = order.Phone,
            CurrentStatus = status,
            LastPosterStatus = status,
            LastCheckedAt = _timeProvider.GetUtcNow().UtcDateTime,
            Language = ResolveLanguage(order.FcmToken)
        });
    }

    private static bool IsTerminalStatus(OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.Cancelled;

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
            (OrderStatus.AwaitingPayment, "ru") => "Ожидаем оплату заказа",
            (OrderStatus.AwaitingPayment, "uz") => "To'lov kutilmoqda",
            (OrderStatus.AwaitingPayment, _) => "Waiting for payment to confirm your order",

            (OrderStatus.Pending, "ru") => "Заказ получен, ждём подтверждения",
            (OrderStatus.Pending, "uz") => "Buyurtmangiz qabul qilindi, tasdiqlash kutilmoqda",
            (OrderStatus.Pending, _) => "Order received, awaiting confirmation",

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
