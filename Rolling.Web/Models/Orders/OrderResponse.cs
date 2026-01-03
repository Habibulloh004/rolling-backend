using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Orders;

/// <summary>
/// Response DTO for order - matches iOS Order model structure
/// </summary>
public sealed class OrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("orderNumber")]
    public string OrderNumber { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public DateTime Date { get; init; }

    [JsonPropertyName("items")]
    public List<OrderItemResponse> Items { get; init; } = new();

    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; init; }

    [JsonPropertyName("deliveryFee")]
    public decimal DeliveryFee { get; init; }

    [JsonPropertyName("discount")]
    public decimal Discount { get; init; }

    [JsonPropertyName("total")]
    public decimal Total { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("timeline")]
    public List<TimelineEventResponse> Timeline { get; init; } = new();

    [JsonPropertyName("deliveryAddress")]
    public string DeliveryAddress { get; init; } = string.Empty;

    [JsonPropertyName("deliveryLatitude")]
    public double? DeliveryLatitude { get; init; }

    [JsonPropertyName("deliveryLongitude")]
    public double? DeliveryLongitude { get; init; }

    [JsonPropertyName("deliveryAddressComment")]
    public string? DeliveryAddressComment { get; init; }

    [JsonPropertyName("paymentMethod")]
    public string PaymentMethod { get; init; } = string.Empty;

    [JsonPropertyName("paymentMethodId")]
    public string? PaymentMethodId { get; init; }

    [JsonPropertyName("paymentTransactionId")]
    public string? PaymentTransactionId { get; init; }

    [JsonPropertyName("paymentErrorCode")]
    public string? PaymentErrorCode { get; init; }

    [JsonPropertyName("paymentErrorMessage")]
    public string? PaymentErrorMessage { get; init; }

    [JsonPropertyName("paymentAttempts")]
    public int PaymentAttempts { get; init; }

    [JsonPropertyName("estimatedDeliveryTime")]
    public DateTime? EstimatedDeliveryTime { get; init; }

    [JsonPropertyName("actualDeliveryTime")]
    public DateTime? ActualDeliveryTime { get; init; }

    [JsonPropertyName("estimatedDeliveryMinutesMin")]
    public int? EstimatedDeliveryMinutesMin { get; init; }

    [JsonPropertyName("estimatedDeliveryMinutesMax")]
    public int? EstimatedDeliveryMinutesMax { get; init; }

    [JsonPropertyName("courier")]
    public CourierResponse? Courier { get; init; }

    [JsonPropertyName("branch")]
    public BranchResponse? Branch { get; init; }

    [JsonPropertyName("posterSpotId")]
    public string? PosterSpotId { get; init; }

    [JsonPropertyName("posterIncomingOrderId")]
    public string? PosterIncomingOrderId { get; init; }

    [JsonPropertyName("posterTransactionId")]
    public string? PosterTransactionId { get; init; }

    [JsonPropertyName("countedTowardsLoyalty")]
    public bool CountedTowardsLoyalty { get; init; }

    [JsonPropertyName("loyaltyPointsSpent")]
    public decimal? LoyaltyPointsSpent { get; init; }

    [JsonPropertyName("loyaltyPointsEarnedPending")]
    public decimal? LoyaltyPointsEarnedPending { get; init; }

    [JsonPropertyName("loyaltyPointsEarnedActual")]
    public decimal? LoyaltyPointsEarnedActual { get; init; }

    [JsonPropertyName("receiptUrl")]
    public string? ReceiptUrl { get; init; }

    [JsonPropertyName("phone")]
    public string Phone { get; init; } = string.Empty;

    [JsonPropertyName("alternatePhone")]
    public string? AlternatePhone { get; init; }

    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    [JsonPropertyName("serviceMode")]
    public int ServiceMode { get; init; }

    [JsonPropertyName("promoCode")]
    public string? PromoCode { get; init; }

    [JsonPropertyName("promoDiscountAmount")]
    public decimal? PromoDiscountAmount { get; init; }

    [JsonPropertyName("promoDiscountPercentage")]
    public double? PromoDiscountPercentage { get; init; }

    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    [JsonPropertyName("userOrderCount")]
    public int? UserOrderCount { get; init; }

    [JsonPropertyName("fcmToken")]
    public string? FcmToken { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Order item response - matches iOS OrderItem structure
/// </summary>
public sealed class OrderItemResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("menuItemId")]
    public string MenuItemId { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("price")]
    public decimal Price { get; init; }

    [JsonPropertyName("modifiers")]
    public string? Modifiers { get; init; }

    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }

    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; init; }

    [JsonPropertyName("isBonus")]
    public bool IsBonus { get; init; }
}

/// <summary>
/// Timeline event response - matches iOS TimelineEvent structure
/// </summary>
public sealed class TimelineEventResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("time")]
    public DateTime? Time { get; init; }

    [JsonPropertyName("isCompleted")]
    public bool IsCompleted { get; init; }

    [JsonPropertyName("isCurrent")]
    public bool IsCurrent { get; init; }
}

/// <summary>
/// Courier response - matches iOS Courier structure
/// </summary>
public sealed class CourierResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("rating")]
    public double Rating { get; init; }

    [JsonPropertyName("photoUrl")]
    public string? PhotoUrl { get; init; }

    [JsonPropertyName("vehicle")]
    public string Vehicle { get; init; } = string.Empty;

    [JsonPropertyName("licensePlate")]
    public string LicensePlate { get; init; } = string.Empty;

    [JsonPropertyName("phone")]
    public string Phone { get; init; } = string.Empty;

    [JsonPropertyName("location")]
    public CourierLocationResponse? Location { get; init; }
}

/// <summary>
/// Courier location response - matches iOS CourierLocation structure
/// </summary>
public sealed class CourierLocationResponse
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }

    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; init; }
}

/// <summary>
/// Branch response - matches iOS Order.Branch structure
/// </summary>
public sealed class BranchResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("address")]
    public string Address { get; init; } = string.Empty;

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }
}
