using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Messaging;
using Rolling.Infrastructure.Poster;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Orders;

public sealed class OrderProcessor
{
    private readonly PosterService _posterService;
    private readonly TelegramService _telegramService;
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(
        PosterService posterService,
        TelegramService telegramService,
        ILogger<OrderProcessor> logger)
    {
        _posterService = posterService;
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task<string?> ProcessAsync(PaymentTransaction order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.OrderDetailsJson))
        {
            return null;
        }

        using var orderDetails = JsonDocument.Parse(string.IsNullOrWhiteSpace(order.OrderDetailsJson) ? "{}" : order.OrderDetailsJson);
        if (!TryGetServiceMode(orderDetails.RootElement, out var serviceMode))
        {
            _logger.LogWarning("Unable to resolve service_mode from order payload. Raw payload: {Payload}", order.OrderDetailsJson);
            return null;
        }

        return serviceMode switch
        {
            1 => await HandleVenueOrderAsync(orderDetails, order.Amount, cancellationToken),
            2 or 3 => await HandleDeliveryOrderAsync(orderDetails, cancellationToken),
            _ => null
        };
    }

    private static bool TryGetServiceMode(JsonElement root, out int serviceMode)
    {
        serviceMode = 0;
        if (!root.TryGetProperty("service_mode", out var element))
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var numeric))
        {
            serviceMode = numeric;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            serviceMode = parsed;
            return true;
        }

        return false;
    }

    private async Task<string?> HandleVenueOrderAsync(JsonDocument orderDetails, decimal amount, CancellationToken cancellationToken)
    {
        var root = orderDetails.RootElement;
        var comment = root.TryGetProperty("comment", out var commentElement) ? commentElement.GetString() ?? string.Empty : string.Empty;
        var spotName = root.TryGetProperty("spot_name", out var spotNameElement) ? spotNameElement.GetString() ?? string.Empty : string.Empty;
        var service = root.TryGetProperty("service", out var serviceElement) ? serviceElement.GetString() : null;

        var response = await _posterService.CreateIncomingOrderAsync(root, cancellationToken);
        var transactionId = response?.RootElement.TryGetProperty("response", out var resp) == true &&
                            resp.TryGetProperty("transaction_id", out var txElement)
            ? txElement.GetString()
            : null;

        var totalAmount = service == "waiter" ? amount + (amount * 0.1m) : amount;
        var message = $"""
📦 Новый заказ!
🛒 Название филиал: {spotName}
📞 Телефон: +998771244444
💵 Сумма заказа: {totalAmount} сум
💳 Метод оплаты: Карта (Оплачено)
🛍 Тип заказа: Заведения
✏️ Комментарий: {comment}
""";

        await _telegramService.SendMessageAsync(message, cancellationToken);
        return transactionId;
    }

    private async Task<string?> HandleDeliveryOrderAsync(JsonDocument orderDetails, CancellationToken cancellationToken)
    {
        var response = await _posterService.CreateIncomingOrderAsync(orderDetails.RootElement, cancellationToken);
        var transactionId = response?.RootElement.TryGetProperty("response", out var resp) == true &&
                            resp.TryGetProperty("transaction_id", out var txElement)
            ? txElement.GetString()
            : null;
        return transactionId;
    }
}
