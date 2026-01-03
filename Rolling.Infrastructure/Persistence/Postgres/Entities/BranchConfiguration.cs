using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// Branch configuration entity for restaurant locations
/// Stores all branch-specific settings including operating hours, delivery, and contact info
/// </summary>
public sealed class BranchConfiguration
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Poster spot ID for POS integration
    /// </summary>
    public int SpotId { get; set; }

    // MARK: - Localized Names
    public string NameEn { get; set; } = string.Empty;
    public string? NameRu { get; set; }
    public string? NameUz { get; set; }

    // MARK: - Localized Short Description
    public string? ShortDescriptionEn { get; set; }
    public string? ShortDescriptionRu { get; set; }
    public string? ShortDescriptionUz { get; set; }

    // MARK: - Status
    public bool IsActive { get; set; } = true;
    public bool IsTemporarilyClosed { get; set; } = false;

    // MARK: - Closure Message (Localized)
    public string? ClosureMessageEn { get; set; }
    public string? ClosureMessageRu { get; set; }
    public string? ClosureMessageUz { get; set; }

    // MARK: - Operating Hours
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
    public string? Timezone { get; set; }
    public bool Is24Hours { get; set; } = false;

    // MARK: - Delivery Settings
    public decimal? DeliveryFee { get; set; }
    public decimal? FreeDeliveryThreshold { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public int? EtaMinMinutes { get; set; }
    public int? EtaMaxMinutes { get; set; }
    public string? EtaDisplayTextEn { get; set; }
    public string? EtaDisplayTextRu { get; set; }
    public string? EtaDisplayTextUz { get; set; }
    public double? DeliveryRadius { get; set; }
    public bool IsDeliveryEnabled { get; set; } = true;

    // MARK: - Pickup Settings
    public bool IsPickupEnabled { get; set; } = true;
    public int? PickupEtaMinutes { get; set; }
    public decimal? PickupDiscountPercentage { get; set; }
    public string? PickupDiscountTextEn { get; set; }
    public string? PickupDiscountTextRu { get; set; }
    public string? PickupDiscountTextUz { get; set; }

    // MARK: - Contact Information
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? Telegram { get; set; }
    public string? Instagram { get; set; }

    // MARK: - Location
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? AddressEn { get; set; }
    public string? AddressRu { get; set; }
    public string? AddressUz { get; set; }
    public string? CityEn { get; set; }
    public string? CityRu { get; set; }
    public string? CityUz { get; set; }
    public string? DistrictEn { get; set; }
    public string? DistrictRu { get; set; }
    public string? DistrictUz { get; set; }
    public string? LandmarkEn { get; set; }
    public string? LandmarkRu { get; set; }
    public string? LandmarkUz { get; set; }
    public string? GoogleMapsUrl { get; set; }
    public string? YandexMapsUrl { get; set; }

    // MARK: - Payment Configuration
    public bool AcceptsCash { get; set; } = true;
    public bool AcceptsCard { get; set; } = true;
    public bool AcceptsOnlinePayment { get; set; } = true;
    public bool AcceptsApplePay { get; set; } = false;
    public bool AcceptsClick { get; set; } = true;
    public bool AcceptsPayme { get; set; } = true;
    public bool AcceptsUzum { get; set; } = false;
    public bool CardOnDelivery { get; set; } = true;

    // MARK: - Feature Flags
    public bool IsDineInEnabled { get; set; } = false;
    public bool IsReservationEnabled { get; set; } = false;
    public bool IsPreOrderEnabled { get; set; } = false;
    public bool IsLoyaltyEnabled { get; set; } = true;
    public bool IsPromoCodesEnabled { get; set; } = true;
    public bool IsGiftCardsEnabled { get; set; } = false;
    public bool IsCateringEnabled { get; set; } = false;
    public bool ShowRatings { get; set; } = true;
    public bool ShowWaitTime { get; set; } = true;

    // MARK: - Media Assets
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? GalleryUrlsJson { get; set; }

    // MARK: - Ordering & Metadata
    public int SortOrder { get; set; } = 0;
    public string? TagsJson { get; set; }
    public string? MetadataJson { get; set; }

    // MARK: - Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
