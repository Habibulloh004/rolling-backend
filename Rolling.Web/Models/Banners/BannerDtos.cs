using System.Text.Json;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Models.Banners;

/// <summary>
/// Localized text fields by language
/// </summary>
public sealed class LocalizedUrls
{
    public string? En { get; set; }
    public string? Ru { get; set; }
    public string? Uz { get; set; }

    public Dictionary<string, string> ToDictionary()
    {
        var dict = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(En)) dict["en"] = En;
        if (!string.IsNullOrEmpty(Ru)) dict["ru"] = Ru;
        if (!string.IsNullOrEmpty(Uz)) dict["uz"] = Uz;
        return dict;
    }

    public static LocalizedUrls FromDictionary(Dictionary<string, string>? dict)
    {
        if (dict == null) return new LocalizedUrls();
        return new LocalizedUrls
        {
            En = dict.GetValueOrDefault("en"),
            Ru = dict.GetValueOrDefault("ru"),
            Uz = dict.GetValueOrDefault("uz")
        };
    }
}

/// <summary>
/// Platform-specific configuration for API responses
/// </summary>
public sealed class PlatformConfigResponse
{
    public LocalizedUrls? ImageUrls { get; set; }
    public LocalizedUrls? Deeplinks { get; set; }

    public static PlatformConfigResponse? FromEntity(BannerPlatformConfig? config)
    {
        if (config == null) return null;
        return new PlatformConfigResponse
        {
            ImageUrls = LocalizedUrls.FromDictionary(config.ImageUrls),
            Deeplinks = LocalizedUrls.FromDictionary(config.Deeplinks)
        };
    }
}

/// <summary>
/// Platform configurations for API responses
/// </summary>
public sealed class PlatformsResponse
{
    public PlatformConfigResponse? Mobile { get; set; }
    public PlatformConfigResponse? Web { get; set; }

    public static PlatformsResponse FromEntity(BannerPlatforms? platforms)
    {
        if (platforms == null) return new PlatformsResponse();
        return new PlatformsResponse
        {
            Mobile = PlatformConfigResponse.FromEntity(platforms.Mobile),
            Web = PlatformConfigResponse.FromEntity(platforms.Web)
        };
    }
}

public sealed class BannerResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Legacy single image URL (for backward compatibility)
    /// </summary>
    public string? ImageUrl { get; set; }

    public string Lang { get; set; } = "ru";

    /// <summary>
    /// Legacy single path/deeplink (for backward compatibility)
    /// </summary>
    public string? Path { get; set; }

    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    /// Language-specific image URLs
    /// </summary>
    public LocalizedUrls? ImageUrls { get; set; }

    /// <summary>
    /// Language-specific deeplinks
    /// </summary>
    public LocalizedUrls? Deeplinks { get; set; }

    /// <summary>
    /// Platform-specific configurations (mobile/web)
    /// </summary>
    public PlatformsResponse? Platforms { get; set; }

    public static BannerResponse FromEntity(Banner banner)
    {
        return new BannerResponse
        {
            Id = banner.Id,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            Description = banner.Description,
            ImageUrl = banner.ImageUrl,
            Lang = banner.Lang,
            Path = banner.Path,
            CreatedAt = banner.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ImageUrls = LocalizedUrls.FromDictionary(banner.GetImageUrls()),
            Deeplinks = LocalizedUrls.FromDictionary(banner.GetDeeplinks()),
            Platforms = PlatformsResponse.FromEntity(banner.GetPlatforms())
        };
    }

    /// <summary>
    /// Creates a response with resolved URLs for a specific language and platform
    /// </summary>
    public static BannerResponse FromEntityForClient(Banner banner, string language, string platform = "mobile")
    {
        return new BannerResponse
        {
            Id = banner.Id,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            Description = banner.Description,
            ImageUrl = banner.GetImageUrlFor(language, platform),
            Lang = banner.Lang,
            Path = banner.GetDeeplinkFor(language, platform),
            CreatedAt = banner.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
            ImageUrls = LocalizedUrls.FromDictionary(banner.GetImageUrls()),
            Deeplinks = LocalizedUrls.FromDictionary(banner.GetDeeplinks()),
            Platforms = PlatformsResponse.FromEntity(banner.GetPlatforms())
        };
    }
}

public sealed class BannersListResponse
{
    public List<BannerResponse> Banners { get; set; } = new();
    public int TotalCount { get; set; }
}

/// <summary>
/// Platform-specific configuration for API requests
/// </summary>
public sealed class PlatformConfigRequest
{
    public LocalizedUrls? ImageUrls { get; set; }
    public LocalizedUrls? Deeplinks { get; set; }

    public BannerPlatformConfig ToEntity()
    {
        return new BannerPlatformConfig
        {
            ImageUrls = ImageUrls?.ToDictionary(),
            Deeplinks = Deeplinks?.ToDictionary()
        };
    }
}

/// <summary>
/// Platform configurations for API requests
/// </summary>
public sealed class PlatformsRequest
{
    public PlatformConfigRequest? Mobile { get; set; }
    public PlatformConfigRequest? Web { get; set; }

    public BannerPlatforms ToEntity()
    {
        return new BannerPlatforms
        {
            Mobile = Mobile?.ToEntity(),
            Web = Web?.ToEntity()
        };
    }
}

public sealed class CreateBannerRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Legacy single image URL (optional, for backward compatibility)
    /// </summary>
    public string? ImageUrl { get; set; }

    public string Lang { get; set; } = "ru";

    /// <summary>
    /// Legacy single path/deeplink (optional, for backward compatibility)
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Language-specific image URLs
    /// </summary>
    public LocalizedUrls? ImageUrls { get; set; }

    /// <summary>
    /// Language-specific deeplinks
    /// </summary>
    public LocalizedUrls? Deeplinks { get; set; }

    /// <summary>
    /// Platform-specific configurations (mobile/web)
    /// </summary>
    public PlatformsRequest? Platforms { get; set; }

    public Banner ToEntity()
    {
        var banner = new Banner
        {
            Title = Title,
            Subtitle = Subtitle,
            Description = Description,
            ImageUrl = ImageUrl,
            Lang = Lang,
            Path = Path,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        };

        if (ImageUrls != null)
        {
            banner.SetImageUrls(ImageUrls.ToDictionary());
        }

        if (Deeplinks != null)
        {
            banner.SetDeeplinks(Deeplinks.ToDictionary());
        }

        if (Platforms != null)
        {
            banner.SetPlatforms(Platforms.ToEntity());
        }

        return banner;
    }
}

public sealed class UpdateBannerRequest
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// Legacy single image URL
    /// </summary>
    public string? ImageUrl { get; set; }

    public string? Lang { get; set; }

    /// <summary>
    /// Legacy single path/deeplink
    /// </summary>
    public string? Path { get; set; }

    public bool? IsActive { get; set; }

    /// <summary>
    /// Language-specific image URLs
    /// </summary>
    public LocalizedUrls? ImageUrls { get; set; }

    /// <summary>
    /// Language-specific deeplinks
    /// </summary>
    public LocalizedUrls? Deeplinks { get; set; }

    /// <summary>
    /// Platform-specific configurations (mobile/web)
    /// </summary>
    public PlatformsRequest? Platforms { get; set; }
}
