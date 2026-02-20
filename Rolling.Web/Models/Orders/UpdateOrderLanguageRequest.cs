namespace Rolling.Web.Models.Orders;

public sealed class UpdateOrderLanguageRequest
{
    public string? OrderId { get; init; }
    public string? OrderNumber { get; init; }
    public string? PosterIncomingOrderId { get; init; }
    public string? PosterTransactionId { get; init; }
    public string Language { get; init; } = string.Empty;
}
