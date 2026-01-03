using System.Text.Json;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Models.BranchConfigs;

#region Response Models

/// <summary>
/// Localized text response with en, ru, uz
/// </summary>
public sealed class LocalizedTextResponse
{
    public string En { get; set; } = string.Empty;
    public string? Ru { get; set; }
    public string? Uz { get; set; }

    public static LocalizedTextResponse Create(string en, string? ru = null, string? uz = null)
    {
        return new LocalizedTextResponse
        {
            En = en,
            Ru = ru ?? en,
            Uz = uz ?? en
        };
    }
}

/// <summary>
/// Operating hours response
/// </summary>
public sealed class OperatingHoursResponse
{
    public string Open { get; set; } = "10:00";
    public string Close { get; set; } = "23:00";
    public string? Timezone { get; set; }
    public bool Is24Hours { get; set; }
}

/// <summary>
/// Delivery settings response
/// </summary>
public sealed class DeliverySettingsResponse
{
    public decimal? Fee { get; set; }
    public decimal? FreeDeliveryThreshold { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public int? EtaMinMinutes { get; set; }
    public int? EtaMaxMinutes { get; set; }
    public LocalizedTextResponse? EtaDisplayText { get; set; }
    public double? DeliveryRadius { get; set; }
    public bool IsDeliveryEnabled { get; set; } = true;
}

/// <summary>
/// Pickup settings response
/// </summary>
public sealed class PickupSettingsResponse
{
    public bool IsPickupEnabled { get; set; } = true;
    public int? EtaMinutes { get; set; }
    public decimal? DiscountPercentage { get; set; }
    public LocalizedTextResponse? DiscountDisplayText { get; set; }
}

/// <summary>
/// Contact information response
/// </summary>
public sealed class ContactInfoResponse
{
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? Telegram { get; set; }
    public string? Instagram { get; set; }
}

/// <summary>
/// Location response
/// </summary>
public sealed class LocationResponse
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public LocalizedTextResponse? Address { get; set; }
    public LocalizedTextResponse? City { get; set; }
    public LocalizedTextResponse? District { get; set; }
    public LocalizedTextResponse? Landmark { get; set; }
    public string? GoogleMapsUrl { get; set; }
    public string? YandexMapsUrl { get; set; }
}

/// <summary>
/// Payment configuration response
/// </summary>
public sealed class PaymentConfigResponse
{
    public bool AcceptsCash { get; set; } = true;
    public bool AcceptsCard { get; set; } = true;
    public bool AcceptsOnlinePayment { get; set; } = true;
    public bool AcceptsApplePay { get; set; } = false;
    public bool AcceptsClick { get; set; } = true;
    public bool AcceptsPayme { get; set; } = true;
    public bool AcceptsUzum { get; set; } = false;
    public bool CardOnDelivery { get; set; } = true;
}

/// <summary>
/// Feature flags response
/// </summary>
public sealed class FeatureFlagsResponse
{
    public bool IsDineInEnabled { get; set; } = false;
    public bool IsReservationEnabled { get; set; } = false;
    public bool IsPreOrderEnabled { get; set; } = false;
    public bool IsLoyaltyEnabled { get; set; } = true;
    public bool IsPromoCodesEnabled { get; set; } = true;
    public bool IsGiftCardsEnabled { get; set; } = false;
    public bool IsCateringEnabled { get; set; } = false;
    public bool ShowRatings { get; set; } = true;
    public bool ShowWaitTime { get; set; } = true;
}

/// <summary>
/// Media assets response
/// </summary>
public sealed class MediaAssetsResponse
{
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ThumbnailUrl { get; set; }
    public List<string>? GalleryUrls { get; set; }
}

/// <summary>
/// Single branch configuration response
/// </summary>
public sealed class BranchConfigurationResponse
{
    public string Id { get; set; } = string.Empty;
    public int SpotId { get; set; }
    public LocalizedTextResponse Name { get; set; } = new();
    public LocalizedTextResponse? ShortDescription { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsTemporarilyClosed { get; set; } = false;
    public LocalizedTextResponse? ClosureMessage { get; set; }
    public OperatingHoursResponse? OperatingHours { get; set; }
    public DeliverySettingsResponse? Delivery { get; set; }
    public PickupSettingsResponse? Pickup { get; set; }
    public ContactInfoResponse? Contact { get; set; }
    public LocationResponse? Location { get; set; }
    public PaymentConfigResponse? Payment { get; set; }
    public FeatureFlagsResponse? Features { get; set; }
    public MediaAssetsResponse? Media { get; set; }
    public int? SortOrder { get; set; }
    public List<string>? Tags { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
    public string? UpdatedAt { get; set; }

    public static BranchConfigurationResponse FromEntity(BranchConfiguration entity)
    {
        return new BranchConfigurationResponse
        {
            Id = entity.Id.ToString(),
            SpotId = entity.SpotId,
            Name = LocalizedTextResponse.Create(entity.NameEn, entity.NameRu, entity.NameUz),
            ShortDescription = !string.IsNullOrEmpty(entity.ShortDescriptionEn)
                ? LocalizedTextResponse.Create(entity.ShortDescriptionEn, entity.ShortDescriptionRu, entity.ShortDescriptionUz)
                : null,
            IsActive = entity.IsActive,
            IsTemporarilyClosed = entity.IsTemporarilyClosed,
            ClosureMessage = !string.IsNullOrEmpty(entity.ClosureMessageEn)
                ? LocalizedTextResponse.Create(entity.ClosureMessageEn, entity.ClosureMessageRu, entity.ClosureMessageUz)
                : null,
            OperatingHours = !string.IsNullOrEmpty(entity.OpenTime) ? new OperatingHoursResponse
            {
                Open = entity.OpenTime ?? "10:00",
                Close = entity.CloseTime ?? "23:00",
                Timezone = entity.Timezone,
                Is24Hours = entity.Is24Hours
            } : null,
            Delivery = new DeliverySettingsResponse
            {
                Fee = entity.DeliveryFee,
                FreeDeliveryThreshold = entity.FreeDeliveryThreshold,
                MinOrderAmount = entity.MinOrderAmount,
                MaxOrderAmount = entity.MaxOrderAmount,
                EtaMinMinutes = entity.EtaMinMinutes,
                EtaMaxMinutes = entity.EtaMaxMinutes,
                EtaDisplayText = !string.IsNullOrEmpty(entity.EtaDisplayTextEn)
                    ? LocalizedTextResponse.Create(entity.EtaDisplayTextEn, entity.EtaDisplayTextRu, entity.EtaDisplayTextUz)
                    : null,
                DeliveryRadius = entity.DeliveryRadius,
                IsDeliveryEnabled = entity.IsDeliveryEnabled
            },
            Pickup = new PickupSettingsResponse
            {
                IsPickupEnabled = entity.IsPickupEnabled,
                EtaMinutes = entity.PickupEtaMinutes,
                DiscountPercentage = entity.PickupDiscountPercentage,
                DiscountDisplayText = !string.IsNullOrEmpty(entity.PickupDiscountTextEn)
                    ? LocalizedTextResponse.Create(entity.PickupDiscountTextEn, entity.PickupDiscountTextRu, entity.PickupDiscountTextUz)
                    : null
            },
            Contact = new ContactInfoResponse
            {
                Phone = entity.Phone,
                AlternatePhone = entity.AlternatePhone,
                Email = entity.Email,
                Whatsapp = entity.Whatsapp,
                Telegram = entity.Telegram,
                Instagram = entity.Instagram
            },
            Location = new LocationResponse
            {
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                Address = !string.IsNullOrEmpty(entity.AddressEn)
                    ? LocalizedTextResponse.Create(entity.AddressEn, entity.AddressRu, entity.AddressUz)
                    : null,
                City = !string.IsNullOrEmpty(entity.CityEn)
                    ? LocalizedTextResponse.Create(entity.CityEn, entity.CityRu, entity.CityUz)
                    : null,
                District = !string.IsNullOrEmpty(entity.DistrictEn)
                    ? LocalizedTextResponse.Create(entity.DistrictEn, entity.DistrictRu, entity.DistrictUz)
                    : null,
                Landmark = !string.IsNullOrEmpty(entity.LandmarkEn)
                    ? LocalizedTextResponse.Create(entity.LandmarkEn, entity.LandmarkRu, entity.LandmarkUz)
                    : null,
                GoogleMapsUrl = entity.GoogleMapsUrl,
                YandexMapsUrl = entity.YandexMapsUrl
            },
            Payment = new PaymentConfigResponse
            {
                AcceptsCash = entity.AcceptsCash,
                AcceptsCard = entity.AcceptsCard,
                AcceptsOnlinePayment = entity.AcceptsOnlinePayment,
                AcceptsApplePay = entity.AcceptsApplePay,
                AcceptsClick = entity.AcceptsClick,
                AcceptsPayme = entity.AcceptsPayme,
                AcceptsUzum = entity.AcceptsUzum,
                CardOnDelivery = entity.CardOnDelivery
            },
            Features = new FeatureFlagsResponse
            {
                IsDineInEnabled = entity.IsDineInEnabled,
                IsReservationEnabled = entity.IsReservationEnabled,
                IsPreOrderEnabled = entity.IsPreOrderEnabled,
                IsLoyaltyEnabled = entity.IsLoyaltyEnabled,
                IsPromoCodesEnabled = entity.IsPromoCodesEnabled,
                IsGiftCardsEnabled = entity.IsGiftCardsEnabled,
                IsCateringEnabled = entity.IsCateringEnabled,
                ShowRatings = entity.ShowRatings,
                ShowWaitTime = entity.ShowWaitTime
            },
            Media = new MediaAssetsResponse
            {
                LogoUrl = entity.LogoUrl,
                BannerUrl = entity.BannerUrl,
                ThumbnailUrl = entity.ThumbnailUrl,
                GalleryUrls = ParseJsonArray(entity.GalleryUrlsJson)
            },
            SortOrder = entity.SortOrder,
            Tags = ParseJsonArray(entity.TagsJson),
            Metadata = ParseJsonDictionary(entity.MetadataJson),
            UpdatedAt = entity.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
    }

    private static List<string>? ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string>? ParseJsonDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// List of branch configurations response
/// </summary>
public sealed class BranchConfigurationsListResponse
{
    public List<BranchConfigurationResponse> Data { get; set; } = new();
    public string? Source { get; set; }
    public bool Cached { get; set; }
    public string? Version { get; set; }
    public string? LastUpdated { get; set; }
}

/// <summary>
/// Single branch response wrapper
/// </summary>
public sealed class SingleBranchConfigurationResponse
{
    public BranchConfigurationResponse Data { get; set; } = new();
    public string? Source { get; set; }
    public bool Cached { get; set; }
}

#endregion

#region Request Models

/// <summary>
/// Create branch configuration request
/// </summary>
public sealed class CreateBranchConfigurationRequest
{
    public int SpotId { get; set; }

    // Names
    public string NameEn { get; set; } = string.Empty;
    public string? NameRu { get; set; }
    public string? NameUz { get; set; }

    // Operating Hours
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
    public string? Timezone { get; set; }
    public bool Is24Hours { get; set; } = false;

    // Delivery
    public decimal? DeliveryFee { get; set; }
    public int? EtaMinMinutes { get; set; }
    public int? EtaMaxMinutes { get; set; }
    public bool IsDeliveryEnabled { get; set; } = true;

    // Pickup
    public bool IsPickupEnabled { get; set; } = true;
    public int? PickupEtaMinutes { get; set; }

    // Contact
    public string? Phone { get; set; }
    public string? Email { get; set; }

    // Location
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? AddressEn { get; set; }
    public string? AddressRu { get; set; }
    public string? AddressUz { get; set; }
    public string? GoogleMapsUrl { get; set; }

    // Ordering
    public int SortOrder { get; set; } = 0;

    public BranchConfiguration ToEntity()
    {
        return new BranchConfiguration
        {
            SpotId = SpotId,
            NameEn = NameEn,
            NameRu = NameRu,
            NameUz = NameUz,
            OpenTime = OpenTime,
            CloseTime = CloseTime,
            Timezone = Timezone,
            Is24Hours = Is24Hours,
            DeliveryFee = DeliveryFee,
            EtaMinMinutes = EtaMinMinutes,
            EtaMaxMinutes = EtaMaxMinutes,
            IsDeliveryEnabled = IsDeliveryEnabled,
            IsPickupEnabled = IsPickupEnabled,
            PickupEtaMinutes = PickupEtaMinutes,
            Phone = Phone,
            Email = Email,
            Latitude = Latitude,
            Longitude = Longitude,
            AddressEn = AddressEn,
            AddressRu = AddressRu,
            AddressUz = AddressUz,
            GoogleMapsUrl = GoogleMapsUrl,
            SortOrder = SortOrder,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Update branch configuration request (all fields optional)
/// </summary>
public sealed class UpdateBranchConfigurationRequest
{
    public int? SpotId { get; set; }

    // Names
    public string? NameEn { get; set; }
    public string? NameRu { get; set; }
    public string? NameUz { get; set; }
    public string? ShortDescriptionEn { get; set; }
    public string? ShortDescriptionRu { get; set; }
    public string? ShortDescriptionUz { get; set; }

    // Status
    public bool? IsActive { get; set; }
    public bool? IsTemporarilyClosed { get; set; }
    public string? ClosureMessageEn { get; set; }
    public string? ClosureMessageRu { get; set; }
    public string? ClosureMessageUz { get; set; }

    // Operating Hours
    public string? OpenTime { get; set; }
    public string? CloseTime { get; set; }
    public string? Timezone { get; set; }
    public bool? Is24Hours { get; set; }

    // Delivery
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
    public bool? IsDeliveryEnabled { get; set; }

    // Pickup
    public bool? IsPickupEnabled { get; set; }
    public int? PickupEtaMinutes { get; set; }
    public decimal? PickupDiscountPercentage { get; set; }

    // Contact
    public string? Phone { get; set; }
    public string? AlternatePhone { get; set; }
    public string? Email { get; set; }
    public string? Whatsapp { get; set; }
    public string? Telegram { get; set; }
    public string? Instagram { get; set; }

    // Location
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? AddressEn { get; set; }
    public string? AddressRu { get; set; }
    public string? AddressUz { get; set; }
    public string? CityEn { get; set; }
    public string? CityRu { get; set; }
    public string? CityUz { get; set; }
    public string? DistrictEn { get; set; }
    public string? DistrictRu { get; set; }
    public string? DistrictUz { get; set; }
    public string? GoogleMapsUrl { get; set; }
    public string? YandexMapsUrl { get; set; }

    // Payment
    public bool? AcceptsCash { get; set; }
    public bool? AcceptsCard { get; set; }
    public bool? AcceptsOnlinePayment { get; set; }
    public bool? AcceptsClick { get; set; }
    public bool? AcceptsPayme { get; set; }
    public bool? AcceptsUzum { get; set; }

    // Features
    public bool? IsDineInEnabled { get; set; }
    public bool? IsReservationEnabled { get; set; }
    public bool? IsLoyaltyEnabled { get; set; }
    public bool? IsPromoCodesEnabled { get; set; }

    // Media
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public string? ThumbnailUrl { get; set; }

    // Ordering
    public int? SortOrder { get; set; }
    public List<string>? Tags { get; set; }
}

#endregion
