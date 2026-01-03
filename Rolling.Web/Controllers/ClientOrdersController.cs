using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

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
    private readonly ILogger<ClientOrdersController> _logger;

    public ClientOrdersController(AppDbContext dbContext, ILogger<ClientOrdersController> logger)
    {
        _dbContext = dbContext;
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
