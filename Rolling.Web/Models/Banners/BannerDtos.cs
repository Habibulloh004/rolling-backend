using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Models.Banners;

public sealed class BannerResponse
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Lang { get; set; } = "ru";
    public string? Path { get; set; }
    public string CreatedAt { get; set; } = string.Empty;

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
            CreatedAt = banner.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }
}

public sealed class BannersListResponse
{
    public List<BannerResponse> Banners { get; set; } = new();
}

public sealed class CreateBannerRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string Lang { get; set; } = "ru";
    public string? Path { get; set; }

    public Banner ToEntity()
    {
        return new Banner
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
    }
}

public sealed class UpdateBannerRequest
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public string? Lang { get; set; }
    public string? Path { get; set; }
    public bool? IsActive { get; set; }
}
