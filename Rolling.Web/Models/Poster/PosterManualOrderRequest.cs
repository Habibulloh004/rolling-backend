using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Poster;

public sealed class PosterManualOrderRequest
{
    [JsonPropertyName("order")]
    public PosterManualOrderPayload? Order { get; init; }
}

public sealed class PosterManualOrderPayload
{
    [JsonPropertyName("orderName")]
    public string? OrderName { get; init; }

    [JsonPropertyName("deliveryInfo")]
    public PosterDeliveryInfo? DeliveryInfo { get; init; }
}

public sealed class PosterDeliveryInfo
{
    [JsonPropertyName("courierId")]
    public long? CourierId { get; init; }
}
