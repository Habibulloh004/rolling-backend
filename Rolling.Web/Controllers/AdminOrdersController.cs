using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.Contracts;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Auth;
using Rolling.Web.Utilities;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/admin/orders")]
[AdminAuthorize]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IChatService _chatService;
    private readonly IConfiguration _configuration;

    public AdminOrdersController(AppDbContext dbContext, IChatService chatService, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _chatService = chatService;
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync(
        [FromQuery] int take = 100,
        [FromQuery] int skip = 0,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int? status = null,
        CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(take, 1, 500);

        var query = _dbContext.Orders.AsNoTracking();

        // Apply search filter (ID, OrderNumber, FirstName, LastName, Phone)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.Trim().ToLower();
            query = query.Where(o =>
                o.Id.ToLower().Contains(searchLower) ||
                o.OrderNumber.ToLower().Contains(searchLower) ||
                (o.FirstName != null && o.FirstName.ToLower().Contains(searchLower)) ||
                (o.LastName != null && o.LastName.ToLower().Contains(searchLower)) ||
                o.Phone.ToLower().Contains(searchLower));
        }

        // Apply date range filter
        if (dateFrom.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt >= fromUtc);
        }

        if (dateTo.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(o => o.CreatedAt <= toUtc);
        }

        // Apply status filter
        if (status.HasValue && Enum.IsDefined(typeof(OrderStatus), status.Value))
        {
            query = query.Where(o => o.Status == (OrderStatus)status.Value);
        }

        // Get total count for pagination info
        var totalCount = await query.CountAsync(cancellationToken);

        var orders = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip(skip)
            .Take(size)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.Date,
                o.Subtotal,
                o.DeliveryFee,
                o.Discount,
                o.Total,
                Status = (int)o.Status,
                StatusName = o.Status.ToString(),
                o.DeliveryAddress,
                o.PaymentMethod,
                o.Phone,
                o.FirstName,
                o.LastName,
                o.BranchName,
                o.ServiceMode,
                o.CreatedAt,
                o.UpdatedAt,
                ItemCount = o.Items.Count
            })
            .ToListAsync(cancellationToken);

        return Ok(new { items = orders, totalCount, take = size, skip });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAsync(string id, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.Timeline)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound(new { error = "Order not found" });
        }

        return Ok(new
        {
            order.Id,
            order.OrderNumber,
            order.Date,
            order.Subtotal,
            order.DeliveryFee,
            order.Discount,
            order.Total,
            Status = (int)order.Status,
            StatusName = order.Status.ToString(),
            order.DeliveryAddress,
            order.DeliveryLatitude,
            order.DeliveryLongitude,
            order.DeliveryAddressComment,
            order.PaymentMethod,
            order.Phone,
            order.AlternatePhone,
            order.FirstName,
            order.LastName,
            order.Comment,
            order.BranchId,
            order.BranchName,
            order.BranchAddress,
            order.BranchPhone,
            order.ServiceMode,
            order.PromoCode,
            order.PromoDiscountAmount,
            order.EstimatedDeliveryTime,
            order.ActualDeliveryTime,
            order.EstimatedDeliveryMinutesMin,
            order.EstimatedDeliveryMinutesMax,
            order.CourierName,
            order.CourierPhone,
            order.CreatedAt,
            order.UpdatedAt,
            Items = order.Items.Select(i => new
            {
                i.Id,
                i.MenuItemId,
                i.Name,
                i.Quantity,
                i.Price,
                i.TotalPrice,
                i.Modifiers,
                i.ImageUrl
            }),
            Timeline = order.Timeline.OrderBy(t => t.SortOrder).Select(t => new
            {
                t.Id,
                t.Title,
                t.Time,
                t.IsCompleted,
                t.IsCurrent
            })
        });
    }

    [HttpPost("{id}/chat")]
    public async Task<IActionResult> OpenOrderChatAsync(string id, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Where(item => item.Id == id)
            .Select(item => new
            {
                item.Id,
                item.OrderNumber,
                item.UserId,
                item.FirstName,
                item.LastName,
                item.Status
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return NotFound(new { error = "Order not found" });
        }

        var tenantId = ResolveTenantId();
        var orderId = DeterministicGuid.From(order.Id);
        var customerSource = string.IsNullOrWhiteSpace(order.UserId) ? order.Id : order.UserId;
        var customerId = DeterministicGuid.From(customerSource!);
        var customerUserId = customerId;
        var customerDisplayName = $"{order.FirstName} {order.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(customerDisplayName))
        {
            customerDisplayName = "Customer";
        }

        var command = new OpenChatThreadCommand(
            tenantId,
            orderId,
            customerId,
            customerUserId,
            customerDisplayName);

        var thread = await _chatService.OpenThreadAsync(command, cancellationToken);
        return Ok(new
        {
            thread.Id,
            OrderId = order.Id,
            order.OrderNumber,
            OrderStatus = (int)order.Status
        });
    }

    private Guid ResolveTenantId()
    {
        var configured = _configuration["ROLLING_TENANT_ID"] ?? _configuration["Rolling:TenantId"];
        if (Guid.TryParse(configured, out var tenantId))
        {
            return tenantId;
        }

        var brand = _configuration["BRAND_NAME"] ?? _configuration["Brand:Name"] ?? "Rolling Sushi";
        return DeterministicGuid.From(brand);
    }
}
