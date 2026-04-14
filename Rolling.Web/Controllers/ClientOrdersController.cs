using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Messaging;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Models.Orders;

namespace Rolling.Web.Controllers;

/// <summary>
/// Client orders API for syncing order statuses between mobile app and backend.
/// This ensures the app always shows the latest order status even if push notifications were missed.
/// </summary>
[ApiController]
[Route("api/client/orders")]
public sealed class ClientOrdersController : ControllerBase
{
    private const int ServiceModeSpot = 1;
    private const int ServiceModeTakeaway = 2;

    private readonly AppDbContext _dbContext;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly TakeawayOrderTracker _takeawayOrderTracker;
    private readonly IOrderUpdatesPublisher _orderUpdatesPublisher;
    private readonly TelegramService _telegramService;
    private readonly ILogger<ClientOrdersController> _logger;

    public ClientOrdersController(
        AppDbContext dbContext,
        ActiveOrderTracker orderTracker,
        TakeawayOrderTracker takeawayOrderTracker,
        IOrderUpdatesPublisher orderUpdatesPublisher,
        TelegramService telegramService,
        ILogger<ClientOrdersController> logger)
    {
        _dbContext = dbContext;
        _orderTracker = orderTracker;
        _takeawayOrderTracker = takeawayOrderTracker;
        _orderUpdatesPublisher = orderUpdatesPublisher;
        _telegramService = telegramService;
        _logger = logger;
    }

    /// <summary>
    /// Get full order list for a customer by phone number.
    /// Used by mobile clients to import orders created outside the app (e.g., admin recreation).
    /// GET /api/client/orders?phone=998901234567
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetOrdersAsync(
        [FromQuery] string phone,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return BadRequest(new { error = "Phone is required" });
        }

        var normalizedPhone = NormalizePhone(phone);
        var size = Math.Clamp(limit, 1, 200);

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Timeline)
            .Where(o =>
                (o.Phone == normalizedPhone || o.Phone == phone) &&
                o.ServiceMode != ServiceModeSpot)
            .OrderByDescending(o => o.CreatedAt)
            .Take(size)
            .ToListAsync(cancellationToken);

        var response = orders.Select(MapOrderResponse).ToList();

        _logger.LogInformation(
            "Fetched {Count} full orders for phone {Phone}",
            response.Count,
            normalizedPhone);

        return Ok(new { orders = response });
    }

    /// <summary>
    /// Get order statuses for a customer by phone number.
    /// Returns only order IDs and their current statuses for efficient syncing.
    /// GET /api/client/orders/statuses?phone=998901234567
    /// </summary>
    [HttpGet("statuses")]
    public async Task<IActionResult> GetOrderStatusesAsync(
        [FromQuery] string phone,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return BadRequest(new { error = "Phone is required" });
        }

        var normalizedPhone = NormalizePhone(phone);

        var statuses = await _dbContext.Orders
            .AsNoTracking()
            .Where(o =>
                (o.Phone == normalizedPhone || o.Phone == phone) &&
                o.ServiceMode != ServiceModeSpot)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new OrderStatusDto
            {
                Id = o.Id,
                OrderNumber = ResolveDisplayOrderNumber(o.PosterIncomingOrderId, o.PosterTransactionId, o.OrderNumber),
                Status = MapStatus(o.Status),
                PosterIncomingOrderId = o.PosterIncomingOrderId,
                PosterTransactionId = o.PosterTransactionId,
                UpdatedAt = o.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Fetched {Count} order statuses for phone {Phone}",
            statuses.Count,
            normalizedPhone);

        return Ok(new { statuses });
    }

    /// <summary>
    /// Cancel an order by backend ID, order number, or Poster identifiers.
    /// POST /api/client/orders/cancel
    /// </summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> CancelOrderAsync(
        [FromBody] CancelOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        var normalizedOrderNumber = NormalizeOrderIdentifier(request.OrderNumber);
        var normalizedIncomingOrderId = NormalizeOrderIdentifier(request.PosterIncomingOrderId);
        var normalizedTransactionId = NormalizeOrderIdentifier(request.PosterTransactionId);

        if (string.IsNullOrWhiteSpace(request.OrderId) &&
            string.IsNullOrWhiteSpace(normalizedOrderNumber) &&
            string.IsNullOrWhiteSpace(normalizedIncomingOrderId) &&
            string.IsNullOrWhiteSpace(normalizedTransactionId))
        {
            return BadRequest(new { error = "orderId, orderNumber, posterIncomingOrderId, or posterTransactionId is required" });
        }

        var order = await _dbContext.Orders.FirstOrDefaultAsync(o =>
                (!string.IsNullOrWhiteSpace(request.OrderId) && o.Id == request.OrderId) ||
                (!string.IsNullOrWhiteSpace(normalizedTransactionId) && o.PosterTransactionId == normalizedTransactionId) ||
                (!string.IsNullOrWhiteSpace(normalizedIncomingOrderId) && o.PosterIncomingOrderId == normalizedIncomingOrderId) ||
                (!string.IsNullOrWhiteSpace(normalizedOrderNumber) &&
                    (o.OrderNumber == normalizedOrderNumber || o.OrderNumber == $"#{normalizedOrderNumber}")),
            cancellationToken);

        if (order is null)
        {
            return NotFound(new { error = "Order not found" });
        }

        if (order.Status == OrderStatus.Delivered)
        {
            return Conflict(new { error = "Order already delivered" });
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            return Ok(new
            {
                cancelled = false,
                status = order.Status.ToString().ToLowerInvariant(),
                orderId = order.Id,
                orderNumber = ResolveDisplayOrderNumber(order.PosterIncomingOrderId, order.PosterTransactionId, order.OrderNumber)
            });
        }

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _orderTracker.UpdateOrderStatus(order.Id, OrderStatus.Cancelled);
        _orderTracker.UntrackOrder(order.Id);
        UpdateTakeawayTracker(order, OrderStatus.Cancelled);

        await _orderUpdatesPublisher.PublishAsync(
            OrderUpdateEventFactory.Create(order, "updated"),
            cancellationToken);

        await TrySendCancelTelegramAsync(order, cancellationToken);

        return Ok(new
        {
            cancelled = true,
            status = order.Status.ToString().ToLowerInvariant(),
            orderId = order.Id,
            orderNumber = ResolveDisplayOrderNumber(order.PosterIncomingOrderId, order.PosterTransactionId, order.OrderNumber)
        });
    }

    private async Task TrySendCancelTelegramAsync(Order order, CancellationToken cancellationToken)
    {
        var paymentDescription = TelegramOrderMessageBuilder.BuildPaymentDescription(order);
        var orderSummary = "Заказ отменён клиентом через веб-сайт.\nТип заказа: Через веб-сайт";
        var context = await TelegramOrderMessageBuilder.CreateContextAsync(
            _dbContext,
            order,
            orderSummary,
            paymentDescription,
            cancellationToken);
        var message = TelegramOrderMessageBuilder.BuildCancelOrderMessage(context);

        try
        {
            await _telegramService.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send Telegram cancel message: OrderId={OrderId}, OrderNumber={OrderNumber}",
                order.Id,
                order.OrderNumber);
        }
    }

    /// <summary>
    /// Update language used for push notifications of an active order.
    /// POST /api/client/orders/language
    /// </summary>
    [HttpPost("language")]
    public async Task<IActionResult> UpdateOrderLanguageAsync(
        [FromBody] UpdateOrderLanguageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Request body is required" });
        }

        var normalizedLanguage = NormalizeLanguage(request.Language);
        if (normalizedLanguage is null)
        {
            return BadRequest(new { error = "Unsupported language. Use: en, uz, or ru" });
        }

        var normalizedOrderNumber = NormalizeOrderIdentifier(request.OrderNumber);
        var normalizedIncomingOrderId = NormalizeOrderIdentifier(request.PosterIncomingOrderId);
        var normalizedTransactionId = NormalizeOrderIdentifier(request.PosterTransactionId);

        if (string.IsNullOrWhiteSpace(request.OrderId) &&
            string.IsNullOrWhiteSpace(normalizedOrderNumber) &&
            string.IsNullOrWhiteSpace(normalizedIncomingOrderId) &&
            string.IsNullOrWhiteSpace(normalizedTransactionId))
        {
            return BadRequest(new { error = "orderId, orderNumber, posterIncomingOrderId, or posterTransactionId is required" });
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o =>
                (!string.IsNullOrWhiteSpace(request.OrderId) && o.Id == request.OrderId) ||
                (!string.IsNullOrWhiteSpace(normalizedTransactionId) && o.PosterTransactionId == normalizedTransactionId) ||
                (!string.IsNullOrWhiteSpace(normalizedIncomingOrderId) && o.PosterIncomingOrderId == normalizedIncomingOrderId) ||
                (!string.IsNullOrWhiteSpace(normalizedOrderNumber) &&
                    (o.OrderNumber == normalizedOrderNumber || o.OrderNumber == $"#{normalizedOrderNumber}")),
                cancellationToken);

        if (order is null)
        {
            return NotFound(new { error = "Order not found" });
        }

        if (order.Status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            return Conflict(new { error = "Order is not active", orderId = order.Id, status = MapStatus(order.Status) });
        }

        var trackerUpdated = _orderTracker.UpdateOrderLanguage(order.Id, normalizedLanguage);
        if (!trackerUpdated)
        {
            _orderTracker.TrackOrder(new TrackedOrder
            {
                OrderId = order.Id,
                OrderNumber = order.OrderNumber,
                PosterIncomingOrderId = order.PosterIncomingOrderId,
                PosterTransactionId = order.PosterTransactionId,
                FcmToken = order.FcmToken,
                Phone = order.Phone,
                CurrentStatus = order.Status,
                ServiceMode = order.ServiceMode,
                LastPosterStatus = order.Status,
                LastCheckedAt = DateTime.UtcNow,
                Language = normalizedLanguage
            });
        }

        UpdateTakeawayTracker(order, order.Status);

        _logger.LogInformation(
            "Updated active order language: OrderId={OrderId}, OrderNumber={OrderNumber}, Language={Language}, TrackerUpdated={TrackerUpdated}",
            order.Id,
            order.OrderNumber,
            normalizedLanguage,
            trackerUpdated);

        return Ok(new
        {
            updated = true,
            orderId = order.Id,
            orderNumber = ResolveDisplayOrderNumber(order.PosterIncomingOrderId, order.PosterTransactionId, order.OrderNumber),
            language = normalizedLanguage
        });
    }

    private void UpdateTakeawayTracker(Order order, OrderStatus status)
    {
        if (order.ServiceMode != ServiceModeTakeaway)
        {
            return;
        }

        if (status is OrderStatus.Delivered or OrderStatus.Cancelled)
        {
            _takeawayOrderTracker.UntrackOrder(order.Id);
            return;
        }

        _takeawayOrderTracker.TrackOrder(order);
    }

    /// <summary>
    /// Get order statuses by list of order IDs.
    /// More efficient for syncing specific orders.
    /// POST /api/client/orders/statuses/batch
    /// </summary>
    [HttpPost("statuses/batch")]
    public async Task<IActionResult> GetOrderStatusesBatchAsync(
        [FromBody] BatchStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.OrderIds == null || request.OrderIds.Count == 0)
        {
            return BadRequest(new { error = "orderIds is required" });
        }

        // Limit to prevent abuse
        var orderIds = request.OrderIds.Take(100).ToList();

        _logger.LogInformation(
            "Batch status request for order IDs: {OrderIds}",
            string.Join(", ", orderIds));

        var statuses = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new OrderStatusDto
            {
                Id = o.Id,
                OrderNumber = ResolveDisplayOrderNumber(o.PosterIncomingOrderId, o.PosterTransactionId, o.OrderNumber),
                Status = MapStatus(o.Status),
                PosterIncomingOrderId = o.PosterIncomingOrderId,
                PosterTransactionId = o.PosterTransactionId,
                UpdatedAt = o.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "Fetched {Count} order statuses for batch request of {RequestCount} orders. Found: {FoundIds}",
            statuses.Count,
            orderIds.Count,
            string.Join(", ", statuses.Select(s => $"{s.OrderNumber}({s.Status})")));

        return Ok(new { statuses });
    }

    private static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.StartsWith("998"))
            return digits;

        if (digits.Length == 9)
            return "998" + digits;

        return digits;
    }

    private static string? NormalizeOrderIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var trimmed = language.Trim().ToLowerInvariant();
        return NotificationService.IsLanguageSupported(trimmed) ? trimmed : null;
    }

    private static string ResolveDisplayOrderNumber(string? posterIncomingOrderId, string? posterTransactionId, string orderNumber)
    {
        if (!string.IsNullOrWhiteSpace(posterTransactionId))
        {
            return posterTransactionId;
        }

        if (!string.IsNullOrWhiteSpace(posterIncomingOrderId))
        {
            return posterIncomingOrderId;
        }

        return orderNumber;
    }

    private static string MapStatus(OrderStatus status) => status switch
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

    private static OrderResponse MapOrderResponse(Order order)
    {
        var timeline = order.Timeline
            .OrderBy(t => t.SortOrder)
            .Select(t => new TimelineEventResponse
            {
                Id = t.Id,
                Title = t.Title,
                Time = t.Time,
                IsCompleted = t.IsCompleted,
                IsCurrent = t.IsCurrent
            })
            .ToList();

        var items = order.Items.Select(item => new OrderItemResponse
        {
            Id = item.Id,
            MenuItemId = item.MenuItemId,
            Name = item.Name,
            Quantity = item.Quantity,
            Price = item.Price,
            TotalPrice = item.TotalPrice,
            Modifiers = item.Modifiers,
            ImageUrl = item.ImageUrl,
            IsBonus = item.IsBonus
        }).ToList();

        CourierResponse? courier = null;
        if (!string.IsNullOrWhiteSpace(order.CourierId) ||
            !string.IsNullOrWhiteSpace(order.CourierName) ||
            !string.IsNullOrWhiteSpace(order.CourierPhone))
        {
            CourierLocationResponse? location = null;
            if (order.CourierLatitude.HasValue && order.CourierLongitude.HasValue && order.CourierLocationLastUpdated.HasValue)
            {
                location = new CourierLocationResponse
                {
                    Latitude = order.CourierLatitude.Value,
                    Longitude = order.CourierLongitude.Value,
                    LastUpdated = order.CourierLocationLastUpdated.Value
                };
            }

            courier = new CourierResponse
            {
                Id = order.CourierId ?? string.Empty,
                Name = order.CourierName ?? string.Empty,
                Rating = order.CourierRating ?? 0,
                PhotoUrl = order.CourierPhotoUrl,
                Vehicle = order.CourierVehicle ?? string.Empty,
                LicensePlate = order.CourierLicensePlate ?? string.Empty,
                Phone = order.CourierPhone ?? string.Empty,
                Location = location
            };
        }

        BranchResponse? branch = null;
        if (!string.IsNullOrWhiteSpace(order.BranchId))
        {
            branch = new BranchResponse
            {
                Id = order.BranchId ?? string.Empty,
                Name = order.BranchName ?? string.Empty,
                Address = order.BranchAddress ?? string.Empty,
                Phone = order.BranchPhone
            };
        }

        return new OrderResponse
        {
            Id = order.Id,
            OrderNumber = ResolveDisplayOrderNumber(order.PosterIncomingOrderId, order.PosterTransactionId, order.OrderNumber),
            Date = order.Date,
            Items = items,
            Subtotal = order.Subtotal,
            DeliveryFee = order.DeliveryFee,
            Discount = order.Discount,
            Total = order.Total,
            Status = MapStatus(order.Status),
            Timeline = timeline,
            DeliveryAddress = order.DeliveryAddress,
            DeliveryLatitude = order.DeliveryLatitude,
            DeliveryLongitude = order.DeliveryLongitude,
            DeliveryAddressComment = order.DeliveryAddressComment,
            PaymentMethod = order.PaymentMethod,
            PaymentMethodId = order.PaymentMethodId,
            PaymentTransactionId = order.PaymentTransactionId,
            PaymentErrorCode = order.PaymentErrorCode,
            PaymentErrorMessage = order.PaymentErrorMessage,
            PaymentAttempts = order.PaymentAttempts,
            EstimatedDeliveryTime = order.EstimatedDeliveryTime,
            ActualDeliveryTime = order.ActualDeliveryTime,
            EstimatedDeliveryMinutesMin = order.EstimatedDeliveryMinutesMin,
            EstimatedDeliveryMinutesMax = order.EstimatedDeliveryMinutesMax,
            Courier = courier,
            Branch = branch,
            PosterSpotId = order.PosterSpotId,
            PosterIncomingOrderId = order.PosterIncomingOrderId,
            PosterTransactionId = order.PosterTransactionId,
            CountedTowardsLoyalty = order.CountedTowardsLoyalty,
            LoyaltyPointsSpent = order.LoyaltyPointsSpent,
            LoyaltyPointsEarnedPending = order.LoyaltyPointsEarnedPending,
            LoyaltyPointsEarnedActual = order.LoyaltyPointsEarnedActual,
            ReceiptUrl = order.ReceiptUrl,
            Phone = order.Phone,
            AlternatePhone = order.AlternatePhone,
            FirstName = order.FirstName,
            LastName = order.LastName,
            Comment = order.Comment,
            ServiceMode = order.ServiceMode,
            PromoCode = order.PromoCode,
            PromoDiscountAmount = order.PromoDiscountAmount,
            PromoDiscountPercentage = order.PromoDiscountPercentage,
            UserId = order.UserId,
            UserOrderCount = order.UserOrderCount,
            FcmToken = order.FcmToken,
            CreatedAt = order.CreatedAt,
            UpdatedAt = order.UpdatedAt
        };
    }
}

public sealed class OrderStatusDto
{
    public string Id { get; init; } = string.Empty;
    public string OrderNumber { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? PosterIncomingOrderId { get; init; }
    public string? PosterTransactionId { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class BatchStatusRequest
{
    public List<string> OrderIds { get; init; } = new();
}
