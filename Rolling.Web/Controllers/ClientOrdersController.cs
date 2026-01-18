using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly AppDbContext _dbContext;
    private readonly ActiveOrderTracker _orderTracker;
    private readonly ILogger<ClientOrdersController> _logger;

    public ClientOrdersController(
        AppDbContext dbContext,
        ActiveOrderTracker orderTracker,
        ILogger<ClientOrdersController> logger)
    {
        _dbContext = dbContext;
        _orderTracker = orderTracker;
        _logger = logger;
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
            .Where(o => o.Phone == normalizedPhone || o.Phone == phone)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new OrderStatusDto
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
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
                orderNumber = order.OrderNumber
            });
        }

        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _orderTracker.UpdateOrderStatus(order.Id, OrderStatus.Cancelled);
        _orderTracker.UntrackOrder(order.Id);

        return Ok(new
        {
            cancelled = true,
            status = order.Status.ToString().ToLowerInvariant(),
            orderId = order.Id,
            orderNumber = order.OrderNumber
        });
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
                OrderNumber = o.OrderNumber,
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
