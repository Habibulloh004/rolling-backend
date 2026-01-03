using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// Order item entity matching iOS OrderItem model structure
/// </summary>
public sealed class OrderItem
{
    [Key]
    public string Id { get; set; } = string.Empty;

    // Reference to parent order
    public string OrderId { get; set; } = string.Empty;

    // Menu Item Reference (Poster product ID)
    public string MenuItemId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Price { get; set; }

    public decimal TotalPrice { get; set; }

    // Modifiers (e.g., "Large, Extra sauce")
    public string? Modifiers { get; set; }

    // Modifier ID for Poster API
    public string? ModifierId { get; set; }

    public string? ImageUrl { get; set; }

    // Is this a bonus/gift item from promotion
    public bool IsBonus { get; set; }

    // Override price for promo items
    public decimal? PriceOverride { get; set; }

    // Navigation Property
    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
}
