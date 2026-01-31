using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Application.Abstractions.Realtime;

namespace Rolling.Web.Realtime;

public sealed class WebSocketOrderUpdatesPublisher : IOrderUpdatesPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly OrderUpdatesConnectionManager _connections;
    private readonly ILogger<WebSocketOrderUpdatesPublisher> _logger;

    public WebSocketOrderUpdatesPublisher(
        OrderUpdatesConnectionManager connections,
        ILogger<WebSocketOrderUpdatesPublisher> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task PublishAsync(OrderUpdateEvent payload, CancellationToken cancellationToken = default)
    {
        var order = payload.Order;
        var message = JsonSerializer.Serialize(new
        {
            type = "orderUpdate",
            payload = new
            {
                changeType = payload.ChangeType,
                order = new
                {
                    order.Id,
                    order.OrderNumber,
                    Date = order.Date?.ToString("O"),
                    order.Subtotal,
                    order.DeliveryFee,
                    order.Discount,
                    order.Total,
                    order.Status,
                    order.StatusName,
                    order.DeliveryAddress,
                    order.PaymentMethod,
                    order.Phone,
                    order.FirstName,
                    order.LastName,
                    order.BranchName,
                    order.ServiceMode,
                    UpdatedAt = order.UpdatedAt.ToString("O"),
                    CreatedAt = order.CreatedAt.ToString("O"),
                    order.ItemCount
                }
            }
        }, JsonOptions);

        await _connections.BroadcastAsync(message, cancellationToken);
        _logger.LogInformation(
            "Broadcasted order update: {OrderId} ({ChangeType})",
            order.Id,
            payload.ChangeType);
    }
}
