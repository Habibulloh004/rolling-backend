using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Orders;

public static class OrderUpdateEventFactory
{
    public static OrderUpdateEvent Create(Order order, string changeType = "updated")
    {
        var payload = new OrderUpdatePayload(
            order.Id,
            order.OrderNumber,
            ToOffsetOrNull(order.Date),
            order.Subtotal,
            order.DeliveryFee,
            order.Discount,
            order.Total,
            (int)order.Status,
            order.Status.ToString(),
            order.DeliveryAddress,
            order.PaymentMethod,
            order.Phone,
            order.FirstName,
            order.LastName,
            order.BranchName,
            order.ServiceMode.ToString(),
            ToOffset(order.UpdatedAt),
            ToOffset(order.CreatedAt),
            order.Items?.Count);

        return new OrderUpdateEvent(payload, changeType);
    }

    private static DateTimeOffset ToOffset(DateTime value)
    {
        if (value == default)
        {
            return DateTimeOffset.UtcNow;
        }

        var utc = value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => value
        };

        return new DateTimeOffset(utc);
    }

    private static DateTimeOffset? ToOffsetOrNull(DateTime value)
    {
        return value == default ? null : ToOffset(value);
    }
}
