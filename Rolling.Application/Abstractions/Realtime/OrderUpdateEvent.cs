namespace Rolling.Application.Abstractions.Realtime;

public sealed record OrderUpdatePayload(
    string Id,
    string? OrderNumber,
    DateTimeOffset? Date,
    decimal? Subtotal,
    decimal? DeliveryFee,
    decimal? Discount,
    decimal? Total,
    int Status,
    string StatusName,
    string? DeliveryAddress,
    string? PaymentMethod,
    string? Phone,
    string? FirstName,
    string? LastName,
    string? BranchName,
    string? ServiceMode,
    DateTimeOffset UpdatedAt,
    DateTimeOffset CreatedAt,
    int? ItemCount);

public sealed record OrderUpdateEvent(
    OrderUpdatePayload Order,
    string ChangeType);

public interface IOrderUpdatesPublisher
{
    Task PublishAsync(OrderUpdateEvent payload, CancellationToken cancellationToken = default);
}
