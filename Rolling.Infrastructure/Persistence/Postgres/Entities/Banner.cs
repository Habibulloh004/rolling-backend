using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class Banner
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Legacy single image URL (kept for backward compatibility)
    /// </summary>
    public string? ImageUrl { get; set; }

    public string Lang { get; set; } = "ru";

    /// <summary>
    /// Legacy single path/deeplink (kept for backward compatibility)
    /// </summary>
    public string? Path { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Language-specific image URLs stored as JSON: {"en": "url", "ru": "url", "uz": "url"}
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string ImageUrls { get; set; } = "{}";

    /// <summary>
    /// Language-specific deeplinks stored as JSON: {"en": "path", "ru": "path", "uz": "path"}
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string Deeplinks { get; set; } = "{}";

    /// <summary>
    /// Platform-specific configuration stored as JSON:
    /// {"mobile": {"imageUrls": {...}, "deeplinks": {...}}, "web": {"imageUrls": {...}, "deeplinks": {...}}}
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string Platforms { get; set; } = "{}";

    // Helper methods to work with localized data

    public Dictionary<string, string> GetImageUrls()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(ImageUrls) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void SetImageUrls(Dictionary<string, string> urls)
    {
        ImageUrls = JsonSerializer.Serialize(urls);
    }

    public Dictionary<string, string> GetDeeplinks()
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(Deeplinks) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void SetDeeplinks(Dictionary<string, string> links)
    {
        Deeplinks = JsonSerializer.Serialize(links);
    }

    public BannerPlatforms GetPlatforms()
    {
        try
        {
            return JsonSerializer.Deserialize<BannerPlatforms>(Platforms) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void SetPlatforms(BannerPlatforms platforms)
    {
        Platforms = JsonSerializer.Serialize(platforms);
    }

    /// <summary>
    /// Gets the image URL for a specific language and platform.
    /// Falls back to: platform-specific -> language-specific -> legacy ImageUrl
    /// </summary>
    public string? GetImageUrlFor(string language, string platform = "mobile")
    {
        var platforms = GetPlatforms();
        var platformConfig = platform.ToLowerInvariant() == "web" ? platforms.Web : platforms.Mobile;

        // Try platform-specific first
        if (platformConfig?.ImageUrls?.TryGetValue(language, out var platformUrl) == true && !string.IsNullOrEmpty(platformUrl))
            return platformUrl;

        // Fall back to language-specific
        var imageUrls = GetImageUrls();
        if (imageUrls.TryGetValue(language, out var langUrl) && !string.IsNullOrEmpty(langUrl))
            return langUrl;

        // Fall back to legacy ImageUrl
        return ImageUrl;
    }

    /// <summary>
    /// Gets the deeplink for a specific language and platform.
    /// Falls back to: platform-specific -> language-specific -> legacy Path
    /// </summary>
    public string? GetDeeplinkFor(string language, string platform = "mobile")
    {
        var platforms = GetPlatforms();
        var platformConfig = platform.ToLowerInvariant() == "web" ? platforms.Web : platforms.Mobile;

        // Try platform-specific first
        if (platformConfig?.Deeplinks?.TryGetValue(language, out var platformLink) == true && !string.IsNullOrEmpty(platformLink))
            return platformLink;

        // Fall back to language-specific
        var deeplinks = GetDeeplinks();
        if (deeplinks.TryGetValue(language, out var langLink) && !string.IsNullOrEmpty(langLink))
            return langLink;

        // Fall back to legacy Path
        return Path;
    }
}

/// <summary>
/// Platform-specific banner configuration
/// </summary>
public sealed class BannerPlatforms
{
    public BannerPlatformConfig? Mobile { get; set; }
    public BannerPlatformConfig? Web { get; set; }
}

/// <summary>
/// Configuration for a specific platform (mobile or web)
/// </summary>
public sealed class BannerPlatformConfig
{
    public Dictionary<string, string>? ImageUrls { get; set; }
    public Dictionary<string, string>? Deeplinks { get; set; }
}