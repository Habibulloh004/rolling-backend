using System.Globalization;
using System.Text.Json;
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
                (o.PosterIncomingOrderId != null && o.PosterIncomingOrderId.ToLower().Contains(searchLower)) ||
                (o.PosterTransactionId != null && o.PosterTransactionId.ToLower().Contains(searchLower)) ||
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
                OrderNumber = o.PosterTransactionId ?? o.PosterIncomingOrderId ?? o.OrderNumber,
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

        var items = order.Items.Select(MapOrderItem).ToList();
        if (items.Count == 0 && !string.IsNullOrWhiteSpace(order.PaymentTransactionId))
        {
            var fallbackItems = await BuildFallbackItemsFromTransactionAsync(order.PaymentTransactionId, cancellationToken);
            if (fallbackItems.Count > 0)
            {
                items = fallbackItems;
            }
        }

        return Ok(new
        {
            order.Id,
            OrderNumber = ResolveDisplayOrderNumber(order.PosterIncomingOrderId, order.PosterTransactionId, order.OrderNumber),
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
            order.PosterIncomingOrderId,
            order.PosterTransactionId,
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
            Items = items,
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
                item.PosterIncomingOrderId,
                item.PosterTransactionId,
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
            OrderNumber = ResolveDisplayOrderNumber(order.PosterIncomingOrderId, order.PosterTransactionId, order.OrderNumber),
            OrderStatus = (int)order.Status
        });
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

    private AdminOrderItemDto MapOrderItem(OrderItem item)
    {
        return new AdminOrderItemDto(
            item.Id,
            item.MenuItemId,
            string.IsNullOrWhiteSpace(item.Name) ? item.MenuItemId : item.Name,
            item.Quantity,
            item.Price,
            item.TotalPrice,
            item.Modifiers,
            item.ImageUrl);
    }

    private async Task<List<AdminOrderItemDto>> BuildFallbackItemsFromTransactionAsync(
        string paymentTransactionId,
        CancellationToken cancellationToken)
    {
        var orderDetailsJson = await _dbContext.Transactions
            .AsNoTracking()
            .Where(transaction => transaction.Id == paymentTransactionId)
            .Select(transaction => transaction.OrderDetailsJson)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(orderDetailsJson))
        {
            return new List<AdminOrderItemDto>();
        }

        try
        {
            using var document = JsonDocument.Parse(orderDetailsJson);
            if (!TryExtractProductsArray(document.RootElement, out var products))
            {
                return new List<AdminOrderItemDto>();
            }

            var items = new List<AdminOrderItemDto>();
            foreach (var product in products.EnumerateArray())
            {
                var menuItemId = ReadString(product, "product_id", "productId", "menu_item_id", "menuItemId", "id");
                if (string.IsNullOrWhiteSpace(menuItemId))
                {
                    continue;
                }

                var quantity = ReadInt(product, "count", "quantity", "qty", "amount") ?? 1;
                if (quantity <= 0)
                {
                    continue;
                }

                var priceOverride = ReadDecimal(product, "price_override", "priceOverride");
                var unitPriceRaw = ReadDecimal(product, "price", "unit_price", "unitPrice");
                var totalPriceRaw = ReadDecimal(product, "total_price", "totalPrice");

                var unitPrice = priceOverride ??
                                unitPriceRaw ??
                                (totalPriceRaw.HasValue
                                    ? Math.Round(totalPriceRaw.Value / quantity, 2, MidpointRounding.AwayFromZero)
                                    : 0m);
                var totalPrice = totalPriceRaw ?? unitPrice * quantity;
                var name = ReadString(product, "name", "product_name", "productName", "title") ?? menuItemId;

                items.Add(new AdminOrderItemDto(
                    Guid.NewGuid().ToString(),
                    menuItemId,
                    name,
                    quantity,
                    unitPrice,
                    totalPrice,
                    ReadString(product, "modifiers"),
                    ReadString(product, "image_url", "imageUrl")));
            }

            return items;
        }
        catch (JsonException)
        {
            return new List<AdminOrderItemDto>();
        }
    }

    private static bool TryExtractProductsArray(JsonElement root, out JsonElement products)
    {
        products = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if ((root.TryGetProperty("products", out products) ||
             root.TryGetProperty("items", out products) ||
             root.TryGetProperty("order_items", out products) ||
             root.TryGetProperty("orderItems", out products)) &&
            products.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if ((root.TryGetProperty("orderDetails", out var nested) ||
             root.TryGetProperty("order_details", out nested)) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            return TryExtractProductsArray(nested, out products);
        }

        return false;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetRawText();
            }
        }

        return null;
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var fractional))
            {
                return (int)Math.Round(fractional, MidpointRounding.AwayFromZero);
            }

            if (element.ValueKind == JsonValueKind.String &&
                int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
            {
                return parsedInt;
            }

            if (element.ValueKind == JsonValueKind.String &&
                double.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                return (int)Math.Round(parsedDouble, MidpointRounding.AwayFromZero);
            }
        }

        return null;
    }

    private static decimal? ReadDecimal(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numeric))
            {
                return numeric;
            }

            if (element.ValueKind == JsonValueKind.String &&
                decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private sealed record AdminOrderItemDto(
        string Id,
        string? MenuItemId,
        string? Name,
        int Quantity,
        decimal Price,
        decimal TotalPrice,
        string? Modifiers,
        string? ImageUrl);

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
