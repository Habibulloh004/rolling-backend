using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Infrastructure.Poster;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Models.Poster;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class CourierOrdersController : ControllerBase
{
    private static readonly ConcurrentDictionary<string, byte> ActiveTransactions = new();
    private static readonly ConcurrentDictionary<string, byte> ManualTransactions = new();

    private readonly AppDbContext _dbContext;
    private readonly PosterService _posterService;
    private readonly ILogger<CourierOrdersController> _logger;

    public CourierOrdersController(
        AppDbContext dbContext,
        PosterService posterService,
        ILogger<CourierOrdersController> logger)
    {
        _dbContext = dbContext;
        _posterService = posterService;
        _logger = logger;
    }

    [HttpPost("")]
    public async Task<IActionResult> HandlePosterWebhookAsync([FromBody] PosterWebhookRequest request, CancellationToken cancellationToken)
    {
        using var payload = ParsePayload(request.Data);
        if (payload is null || string.IsNullOrWhiteSpace(request.ObjectId))
        {
            return Ok(new { success = true });
        }

        if (!ShouldProcessWebhook(payload.RootElement))
        {
            return Ok(new { success = true });
        }

        var transactionId = request.ObjectId;
        if (!ActiveTransactions.TryAdd(transactionId, 0))
        {
            _logger.LogInformation("Transaction {TransactionId} is already being processed.", transactionId);
            return Ok(new { success = true, status = "already_processing" });
        }

        try
        {
            var createdOrder = await CreateOrderFromTransactionAsync(transactionId, null, cancellationToken);
            return Ok(new { success = createdOrder is not null, alreadyExists = createdOrder is null });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process transaction {TransactionId}", transactionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
        finally
        {
            ActiveTransactions.TryRemove(transactionId, out _);
        }
    }

    [HttpPost("posterFromMe")]
    public async Task<IActionResult> HandlePosterOrderAsync([FromBody] PosterManualOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Order?.OrderName is null)
        {
            return BadRequest(new { error = "order.orderName is required" });
        }

        var transactionId = request.Order.OrderName;
        if (!ManualTransactions.TryAdd(transactionId, 0))
        {
            return Ok(new { success = true, status = "already_processing" });
        }

        try
        {
            var fallbackCourierId = request.Order.DeliveryInfo?.CourierId;
            var createdOrder = await CreateOrderFromTransactionAsync(transactionId, fallbackCourierId, cancellationToken);
            if (createdOrder is null)
            {
                return Ok(new { success = false, error = "Order already exists or missing courier id" });
            }

            return Ok(ToOrderResponse(createdOrder));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle manual order {TransactionId}", transactionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
        }
        finally
        {
            ManualTransactions.TryRemove(transactionId, out _);
        }
    }

    [HttpPost("socketData")]
    public IActionResult ReceiveSocketData([FromBody] JsonElement payload)
    {
        _logger.LogInformation("Received socket data payload: {Payload}", payload.GetRawText());
        return Ok(new { success = true });
    }

    [HttpPost("deleteOrder")]
    public async Task<IActionResult> DeleteOrderByCommentAsync([FromBody] DeleteOrderByCommentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Comment))
        {
            return BadRequest(new { error = "comment is required" });
        }

        var pattern = $"%\"transaction_comment\":\"{EscapeLikePattern(request.Comment)}\"%";
        var deleted = await _dbContext.CourierOrders
            .Where(order => order.OrderDataJson != null && EF.Functions.ILike(order.OrderDataJson!, pattern, "\\"))
            .ExecuteDeleteAsync(cancellationToken);

        return Ok(new { deleted });
    }

    [HttpPost("postFromStark")]
    public IActionResult ReceiveStarkOrder([FromBody] JsonElement payload)
    {
        _logger.LogInformation("Received Stark payload: {Payload}", payload.GetRawText());
        return Ok(new { success = true });
    }

    [HttpGet("getOrders/{id}")]
    public async Task<IActionResult> GetCourierOrdersAsync(long id, CancellationToken cancellationToken)
    {
        var orders = await _dbContext.CourierOrders
            .AsNoTracking()
            .Where(order => order.CourierId == id)
            .ToListAsync(cancellationToken);

        return Ok(orders.Select(ToOrderResponse));
    }

    [HttpGet("getTransaction")]
    public async Task<IActionResult> GetTransactionsAsync([FromQuery] string id, [FromQuery] string date, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(date))
        {
            return BadRequest(new { error = "id and date query parameters are required" });
        }

        var document = await _posterService.GetTransactionsAsync(id, date, date, cancellationToken);
        if (document is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Poster service unavailable" });
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Array)
            {
                return Ok(Array.Empty<object>());
            }

            var tasks = response.EnumerateArray()
                .Select(transaction => EnrichTransactionAsync(transaction, cancellationToken));

            var results = await Task.WhenAll(tasks);
            return Ok(results);
        }
    }

    [HttpPut("changeStatus/{orderId}")]
    public async Task<IActionResult> ChangeOrderStatusAsync(string orderId, [FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        if (!payload.TryGetProperty("status", out var statusElement))
        {
            return BadRequest(new { error = "status is required" });
        }

        var status = statusElement.GetString() ?? "waiting";
        var updated = await _dbContext.CourierOrders
            .Where(order => order.OrderId == orderId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(order => order.Status, status), cancellationToken);

        return Ok(new { updated });
    }

    [HttpDelete("deleteOrder/{orderId}")]
    public async Task<IActionResult> DeleteOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        var deleted = await _dbContext.CourierOrders
            .Where(order => order.OrderId == orderId)
            .ExecuteDeleteAsync(cancellationToken);

        return Ok(new { deleted });
    }

    [HttpDelete("deleteOrders/{clientId}")]
    public async Task<IActionResult> DeleteCourierOrdersAsync(long clientId, CancellationToken cancellationToken)
    {
        var deleted = await _dbContext.CourierOrders
            .Where(order => order.CourierId == clientId)
            .ExecuteDeleteAsync(cancellationToken);

        return Ok(new { deleted, clientId });
    }

    [HttpGet("findOrder/{orderId}")]
    public async Task<IActionResult> FindOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.CourierOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.OrderId == orderId, cancellationToken);

        return Ok(order is null ? null : ToOrderResponse(order));
    }

    private async Task<CourierOrder?> CreateOrderFromTransactionAsync(string transactionId, long? fallbackCourierId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.CourierOrders
            .AsNoTracking()
            .FirstOrDefaultAsync(order => order.OrderId == transactionId, cancellationToken);

        if (existing is not null)
        {
            return null;
        }

        var transactionDoc = await _posterService.GetTransactionAsync(transactionId, cancellationToken);
        if (transactionDoc is null)
        {
            throw new InvalidOperationException("Failed to retrieve transaction from Poster.");
        }

        using var json = transactionDoc;
        if (!json.RootElement.TryGetProperty("response", out var response) || response.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Poster transaction payload is invalid.");
        }

        var transaction = response.EnumerateArray().FirstOrDefault();
        if (transaction.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException("Poster transaction payload is empty.");
        }

        var orderId = transaction.TryGetProperty("transaction_id", out var orderIdElement)
            ? orderIdElement.GetString() ?? transactionId
            : transactionId;

        if (!string.Equals(orderId, transactionId, StringComparison.Ordinal))
        {
            var duplicate = await _dbContext.CourierOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(order => order.OrderId == orderId, cancellationToken);

            if (duplicate is not null)
            {
                return null;
            }
        }

        var courierId = GetCourierId(transaction) ?? fallbackCourierId;
        if (courierId is null)
        {
            return null;
        }

        using var productsDoc = await _posterService.GetTransactionProductsAsync(orderId, cancellationToken);
        var productsJson = productsDoc is not null && productsDoc.RootElement.TryGetProperty("response", out var productElement)
            ? productElement.GetRawText()
            : "[]";

        var entity = new CourierOrder
        {
            OrderId = orderId,
            CourierId = courierId.Value,
            OrderDataJson = transaction.GetRawText(),
            ProductsJson = productsJson,
            Status = "waiting"
        };

        await _dbContext.CourierOrders.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static long? GetCourierId(JsonElement transaction)
    {
        if (!transaction.TryGetProperty("delivery", out var delivery) || delivery.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (delivery.TryGetProperty("courier_id", out var courierElement))
        {
            switch (courierElement.ValueKind)
            {
                case JsonValueKind.String when long.TryParse(courierElement.GetString(), out var parsed):
                    return parsed;
                case JsonValueKind.Number when courierElement.TryGetInt64(out var value):
                    return value;
            }
        }

        return null;
    }

    private static JsonDocument? ParsePayload(JsonElement? element)
    {
        if (element is null)
        {
            return null;
        }

        try
        {
            return element.Value.ValueKind switch
            {
                JsonValueKind.String when !string.IsNullOrWhiteSpace(element.Value.GetString()) => JsonDocument.Parse(element.Value.GetString()!),
                JsonValueKind.Object => JsonDocument.Parse(element.Value.GetRawText()),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ShouldProcessWebhook(JsonElement payload)
    {
        if (!payload.TryGetProperty("transactions_history", out var history) || history.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var typeMatches = history.TryGetProperty("type_history", out var typeElement) &&
                          typeElement.GetString() == "changeprocessingstatus";

        var valueMatches = history.TryGetProperty("value", out var valueElement) &&
                           valueElement.ValueKind is JsonValueKind.Number && valueElement.GetInt32() == 40;

        return typeMatches && valueMatches;
    }

    private async Task<JsonNode> EnrichTransactionAsync(JsonElement transaction, CancellationToken cancellationToken)
    {
        var node = JsonNode.Parse(transaction.GetRawText()) as JsonObject ?? new JsonObject();
        var transactionId = node["transaction_id"]?.GetValue<string>();

        if (!string.IsNullOrWhiteSpace(transactionId))
        {
            using var productsDoc = await _posterService.GetTransactionProductsAsync(transactionId, cancellationToken);
            if (productsDoc is not null && productsDoc.RootElement.TryGetProperty("response", out var productsElement))
            {
                node["products_name"] = JsonNode.Parse(productsElement.GetRawText());
            }
            else
            {
                node["products_name"] = new JsonArray();
            }
        }

        var comment = node["transaction_comment"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(comment))
        {
            using var backOrderDoc = await _posterService.GetAlternateOrderAsync(comment, cancellationToken);
            if (backOrderDoc is not null)
            {
                node["backOrder"] = JsonNode.Parse(backOrderDoc.RootElement.GetRawText());
            }
        }

        return node;
    }

    private static object ToOrderResponse(CourierOrder order) => new
    {
        order_id = order.OrderId,
        courier_id = order.CourierId,
        orderData = DeserializeJson(order.OrderDataJson),
        products = DeserializeJson(order.ProductsJson),
        status = order.Status
    };

    private static object? DeserializeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<object>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string EscapeLikePattern(string value) =>
        value
            .Replace(@"\", @"\\")
            .Replace("%", @"\%")
            .Replace("_", @"\_");
}
