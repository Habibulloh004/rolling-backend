using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Orders;

/// <summary>
/// Request DTO for creating an order - matches iOS PosterIncomingOrderRequest structure
/// </summary>
public sealed class CreateOrderRequest
{
    /// <summary>
    /// Poster spot/branch ID
    /// </summary>
    [JsonPropertyName("spotId")]
    public string SpotId { get; init; } = string.Empty;

    /// <summary>
    /// Primary phone number (required, sanitized, minimum 10 digits)
    /// </summary>
    [JsonPropertyName("phone")]
    public string Phone { get; init; } = string.Empty;

    /// <summary>
    /// Secondary phone number (optional)
    /// </summary>
    [JsonPropertyName("alternatePhone")]
    public string? AlternatePhone { get; init; }

    /// <summary>
    /// Customer first name (max 60 chars)
    /// </summary>
    [JsonPropertyName("firstName")]
    public string? FirstName { get; init; }

    /// <summary>
    /// Customer last name (max 60 chars)
    /// </summary>
    [JsonPropertyName("lastName")]
    public string? LastName { get; init; }

    /// <summary>
    /// Full delivery address (max 250 chars)
    /// </summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>
    /// Order comments/special instructions
    /// </summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; init; }

    /// <summary>
    /// Delivery fee in UZS
    /// </summary>
    [JsonPropertyName("deliveryPrice")]
    public decimal DeliveryPrice { get; init; }

    /// <summary>
    /// Payment method ID ("1" for cash, "2" for card)
    /// </summary>
    [JsonPropertyName("paymentMethodId")]
    public string? PaymentMethodId { get; init; }

    /// <summary>
    /// Products in the order
    /// </summary>
    [JsonPropertyName("products")]
    public List<CreateOrderProductRequest> Products { get; init; } = new();

    /// <summary>
    /// Applied promotions
    /// </summary>
    [JsonPropertyName("promotions")]
    public List<CreateOrderPromotionRequest>? Promotions { get; init; }

    /// <summary>
    /// Service mode: 1 = dine-in, 2 = pickup, 3 = delivery
    /// </summary>
    [JsonPropertyName("serviceMode")]
    public int ServiceMode { get; init; } = 3;

    /// <summary>
    /// Delivery latitude coordinate
    /// </summary>
    [JsonPropertyName("deliveryLatitude")]
    public double? DeliveryLatitude { get; init; }

    /// <summary>
    /// Delivery longitude coordinate
    /// </summary>
    [JsonPropertyName("deliveryLongitude")]
    public double? DeliveryLongitude { get; init; }

    /// <summary>
    /// Address landmark or special instructions
    /// </summary>
    [JsonPropertyName("deliveryAddressComment")]
    public string? DeliveryAddressComment { get; init; }

    /// <summary>
    /// Loyalty points to redeem
    /// </summary>
    [JsonPropertyName("loyaltyPointsUsed")]
    public decimal? LoyaltyPointsUsed { get; init; }

    /// <summary>
    /// Promo code applied
    /// </summary>
    [JsonPropertyName("promoCode")]
    public string? PromoCode { get; init; }

    /// <summary>
    /// Promo discount amount
    /// </summary>
    [JsonPropertyName("promoDiscountAmount")]
    public decimal? PromoDiscountAmount { get; init; }

    /// <summary>
    /// Promo discount percentage
    /// </summary>
    [JsonPropertyName("promoDiscountPercentage")]
    public double? PromoDiscountPercentage { get; init; }

    /// <summary>
    /// User ID
    /// </summary>
    [JsonPropertyName("userId")]
    public string? UserId { get; init; }

    /// <summary>
    /// User's total order count
    /// </summary>
    [JsonPropertyName("userOrderCount")]
    public int? UserOrderCount { get; init; }

    /// <summary>
    /// Current device FCM token for push notifications
    /// </summary>
    [JsonPropertyName("fcmToken")]
    public string? FcmToken { get; init; }

    /// <summary>
    /// Preferred language for notifications (en, ru, uz)
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>
    /// Saved card token for payment
    /// </summary>
    [JsonPropertyName("savedCardToken")]
    public string? SavedCardToken { get; init; }

    /// <summary>
    /// Scheduled delivery datetime
    /// </summary>
    [JsonPropertyName("scheduledDeliveryTime")]
    public DateTime? ScheduledDeliveryTime { get; init; }

    /// <summary>
    /// Cutlery quantity
    /// </summary>
    [JsonPropertyName("cutleryQuantity")]
    public int? CutleryQuantity { get; init; }

    /// <summary>
    /// Napkins quantity
    /// </summary>
    [JsonPropertyName("napkinsQuantity")]
    public int? NapkinsQuantity { get; init; }

    /// <summary>
    /// Delivery instructions
    /// </summary>
    [JsonPropertyName("deliveryInstructions")]
    public string? DeliveryInstructions { get; init; }

    /// <summary>
    /// Order subtotal
    /// </summary>
    [JsonPropertyName("subtotal")]
    public decimal Subtotal { get; init; }

    /// <summary>
    /// Total discount amount
    /// </summary>
    [JsonPropertyName("discount")]
    public decimal Discount { get; init; }

    /// <summary>
    /// Order total
    /// </summary>
    [JsonPropertyName("total")]
    public decimal Total { get; init; }

    /// <summary>
    /// Branch ID
    /// </summary>
    [JsonPropertyName("branchId")]
    public string? BranchId { get; init; }

    /// <summary>
    /// Branch name
    /// </summary>
    [JsonPropertyName("branchName")]
    public string? BranchName { get; init; }

    /// <summary>
    /// Branch address
    /// </summary>
    [JsonPropertyName("branchAddress")]
    public string? BranchAddress { get; init; }

    /// <summary>
    /// Branch phone
    /// </summary>
    [JsonPropertyName("branchPhone")]
    public string? BranchPhone { get; init; }
}

/// <summary>
/// Product in order request - matches iOS Product structure
/// </summary>
public sealed class CreateOrderProductRequest
{
    /// <summary>
    /// Poster product ID
    /// </summary>
    [JsonPropertyName("productId")]
    public string ProductId { get; init; } = string.Empty;

    /// <summary>
    /// Product name
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Quantity
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>
    /// Modifier ID (for Poster API)
    /// </summary>
    [JsonPropertyName("modificatorId")]
    public string? ModificatorId { get; init; }

    /// <summary>
    /// Modifier description (e.g., "Large, Extra sauce")
    /// </summary>
    [JsonPropertyName("modifiers")]
    public string? Modifiers { get; init; }

    /// <summary>
    /// Override price for promo items
    /// </summary>
    [JsonPropertyName("priceOverride")]
    public decimal? PriceOverride { get; init; }

    /// <summary>
    /// Is bonus/gift product from promotion
    /// </summary>
    [JsonPropertyName("isGift")]
    public bool IsGift { get; init; }

    /// <summary>
    /// Unit price
    /// </summary>
    [JsonPropertyName("price")]
    public decimal Price { get; init; }

    /// <summary>
    /// Total price (price * count)
    /// </summary>
    [JsonPropertyName("totalPrice")]
    public decimal TotalPrice { get; init; }

    /// <summary>
    /// Product image URL
    /// </summary>
    [JsonPropertyName("imageUrl")]
    public string? ImageUrl { get; init; }
}

/// <summary>
/// Promotion in order request - matches iOS Promotion structure
/// </summary>
public sealed class CreateOrderPromotionRequest
{
    /// <summary>
    /// Promotion ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Products involved in the promotion condition
    /// </summary>
    [JsonPropertyName("involvedProducts")]
    public List<PromotionProductRequest> InvolvedProducts { get; init; } = new();

    /// <summary>
    /// Products resulting from the promotion (bonus items)
    /// </summary>
    [JsonPropertyName("resultProducts")]
    public List<PromotionProductRequest> ResultProducts { get; init; } = new();
}

/// <summary>
/// Product in promotion request
/// </summary>
public sealed class PromotionProductRequest
{
    /// <summary>
    /// Product ID
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Quantity
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; init; }

    /// <summary>
    /// Modification ID
    /// </summary>
    [JsonPropertyName("modificationId")]
    public string? ModificationId { get; init; }
}
