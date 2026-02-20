using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// Order status matching iOS OrderStatus enum
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 0,
    Pending = 1,
    Accepted = 2,
    Preparing = 3,
    OnTheWay = 4,
    Delivered = 5,
    Cancelled = 6
}

/// <summary>
/// Order entity matching iOS Order model structure
/// </summary>
public sealed class Order
{
    [Key]
    public string Id { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public DateTime Date { get; set; } = DateTime.UtcNow;

    // Financial Data
    public decimal Subtotal { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    // Status
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    // Delivery Information
    public string DeliveryAddress { get; set; } = string.Empty;

    public double? DeliveryLatitude { get; set; }

    public double? DeliveryLongitude { get; set; }

    public string? DeliveryAddressComment { get; set; }

    // Payment Information
    public string PaymentMethod { get; set; } = string.Empty;

    public string? PaymentMethodId { get; set; }

    public string? PaymentTransactionId { get; set; }

    public string? PaymentErrorCode { get; set; }

    public string? PaymentErrorMessage { get; set; }

    public int PaymentAttempts { get; set; }

    public string? SavedCardToken { get; set; }

    // Delivery Time
    public DateTime? EstimatedDeliveryTime { get; set; }

    public DateTime? ActualDeliveryTime { get; set; }

    public int? EstimatedDeliveryMinutesMin { get; set; }

    public int? EstimatedDeliveryMinutesMax { get; set; }

    // Branch Information
    public string? BranchId { get; set; }

    public string? BranchName { get; set; }

    public string? BranchAddress { get; set; }

    public string? BranchPhone { get; set; }

    // Poster Integration
    public string? PosterSpotId { get; set; }

    public string? PosterIncomingOrderId { get; set; }

    public string? PosterTransactionId { get; set; }

    // Loyalty Program
    public bool CountedTowardsLoyalty { get; set; }

    public decimal? LoyaltyPointsSpent { get; set; }

    public decimal? LoyaltyPointsEarnedPending { get; set; }

    public decimal? LoyaltyPointsEarnedActual { get; set; }

    // Receipt
    public string? ReceiptUrl { get; set; }

    // Contact Information
    public string Phone { get; set; } = string.Empty;

    public string? AlternatePhone { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    // Order Comment/Instructions
    public string? Comment { get; set; }

    // Courier Information (stored as JSON or separate table)
    public string? CourierId { get; set; }

    public string? CourierName { get; set; }

    public double? CourierRating { get; set; }

    public string? CourierPhotoUrl { get; set; }

    public string? CourierVehicle { get; set; }

    public string? CourierLicensePlate { get; set; }

    public string? CourierPhone { get; set; }

    public double? CourierLatitude { get; set; }

    public double? CourierLongitude { get; set; }

    public DateTime? CourierLocationLastUpdated { get; set; }

    // Service Mode (1 = dine-in, 2 = pickup, 3 = delivery)
    public int ServiceMode { get; set; } = 3;

    // Takeaway automation timestamps (for restart-safe virtual statuses)
    public DateTime? TakeawayAcceptedAt { get; set; }

    public DateTime? TakeawayPreparingAt { get; set; }

    public DateTime? TakeawayReadyForPickupAt { get; set; }

    // Promo Code
    public string? PromoCode { get; set; }

    public decimal? PromoDiscountAmount { get; set; }

    public double? PromoDiscountPercentage { get; set; }

    // User Information
    public string? UserId { get; set; }

    public int? UserOrderCount { get; set; }

    // Device push token (FCM) for order notifications
    public string? FcmToken { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();

    public ICollection<OrderTimelineEvent> Timeline { get; set; } = new List<OrderTimelineEvent>();
}
