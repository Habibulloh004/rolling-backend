using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Persistence.Postgres;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public AdminOrdersController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<IActionResult> ListAsync(
        [FromQuery] int take = 100,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var size = Math.Clamp(take, 1, 500);

        var orders = await _dbContext.Orders
            .AsNoTracking()
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

        return Ok(orders);
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
}
