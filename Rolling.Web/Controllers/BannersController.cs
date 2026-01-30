using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Cache;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Redis;
using Rolling.Web.Models.Banners;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class BannersController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IRedisBannerCache _cache;
    private readonly ICacheRevalidationPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BannersController> _logger;

    public BannersController(
        AppDbContext dbContext,
        IRedisBannerCache cache,
        ICacheRevalidationPublisher publisher,
        TimeProvider timeProvider,
        ILogger<BannersController> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Get all active banners, sorted by creation date (newest first)
    /// Supports filtering by language, search text, and date range
    /// </summary>
    /// <param name="lang">Filter by language code (en, ru, uz)</param>
    /// <param name="platform">Platform type for URL resolution: "mobile" (default) or "web"</param>
    /// <param name="search">Search text in title, subtitle, description</param>
    /// <param name="dateFrom">Filter by creation date from</param>
    /// <param name="dateTo">Filter by creation date to</param>
    /// <param name="take">Number of items to return (max 500)</param>
    /// <param name="skip">Number of items to skip</param>
    /// <param name="resolve">If true, resolves imageUrl and path to language/platform-specific values</param>
    [HttpGet]
    public async Task<IActionResult> GetBannersAsync(
        [FromQuery] string? lang = null,
        [FromQuery] string? platform = "mobile",
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] int take = 100,
        [FromQuery] int skip = 0,
        [FromQuery] bool resolve = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectivePlatform = string.IsNullOrWhiteSpace(platform) ? "mobile" : platform;

            // Skip cache if using search or date filters for accurate results
            var hasFilters = !string.IsNullOrWhiteSpace(search) || dateFrom.HasValue || dateTo.HasValue || skip > 0;

            if (!hasFilters)
            {
                // Try to get from Redis cache first (only for simple lang queries)
                var cachedBanners = await _cache.GetBannersAsync(lang, cancellationToken);
                if (cachedBanners != null)
                {
                    var size = Math.Clamp(take, 1, 500);
                    var cachedList = cachedBanners.ToList();
                    var cachedResponse = new BannersListResponse
                    {
                        Banners = cachedList.Take(size).Select(b =>
                            resolve && !string.IsNullOrWhiteSpace(lang)
                                ? BannerResponse.FromEntityForClient(b, lang, effectivePlatform)
                                : BannerResponse.FromEntity(b)).ToList(),
                        TotalCount = cachedList.Count
                    };
                    return Ok(cachedResponse);
                }
            }

            // Cache miss or using filters - fetch from database
            var query = _dbContext.Banners
                .AsNoTracking()
                .Where(b => b.IsActive);

            // Apply language filter
            if (!string.IsNullOrWhiteSpace(lang))
            {
                query = query.Where(b => b.Lang == lang);
            }

            // Apply search filter (Title, Subtitle, Description)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.Trim().ToLower();
                query = query.Where(b =>
                    b.Title.ToLower().Contains(searchLower) ||
                    (b.Subtitle != null && b.Subtitle.ToLower().Contains(searchLower)) ||
                    b.Description.ToLower().Contains(searchLower));
            }

            // Apply date range filter
            if (dateFrom.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(dateFrom.Value.Date, DateTimeKind.Utc);
                query = query.Where(b => b.CreatedAt >= fromUtc);
            }

            if (dateTo.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(dateTo.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
                query = query.Where(b => b.CreatedAt <= toUtc);
            }

            // Get total count for pagination
            var totalCount = await query.CountAsync(cancellationToken);

            var size2 = Math.Clamp(take, 1, 500);
            var banners = await query
                .OrderByDescending(b => b.CreatedAt)
                .Skip(skip)
                .Take(size2)
                .ToListAsync(cancellationToken);

            // Only cache if no filters applied (simple queries)
            if (!hasFilters)
            {
                await _cache.SetBannersAsync(banners, lang, cancellationToken);
            }

            var response = new BannersListResponse
            {
                Banners = banners.Select(b =>
                    resolve && !string.IsNullOrWhiteSpace(lang)
                        ? BannerResponse.FromEntityForClient(b, lang, effectivePlatform)
                        : BannerResponse.FromEntity(b)).ToList(),
                TotalCount = totalCount
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching banners");
            return StatusCode(500, new { error = "Failed to fetch banners" });
        }
    }

    /// <summary>
    /// Get a specific banner by ID
    /// </summary>
    /// <param name="id">Banner ID</param>
    /// <param name="lang">Language code for URL resolution (en, ru, uz)</param>
    /// <param name="platform">Platform type for URL resolution: "mobile" (default) or "web"</param>
    /// <param name="resolve">If true, resolves imageUrl and path to language/platform-specific values</param>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBannerAsync(
        int id,
        [FromQuery] string? lang = null,
        [FromQuery] string? platform = "mobile",
        [FromQuery] bool resolve = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var effectivePlatform = string.IsNullOrWhiteSpace(platform) ? "mobile" : platform;

            // Try to get from Redis cache first
            var cachedBanner = await _cache.GetBannerByIdAsync(id, cancellationToken);
            if (cachedBanner != null)
            {
                var response = resolve && !string.IsNullOrWhiteSpace(lang)
                    ? BannerResponse.FromEntityForClient(cachedBanner, lang, effectivePlatform)
                    : BannerResponse.FromEntity(cachedBanner);
                return Ok(response);
            }

            // Cache miss - fetch from database
            var banner = await _dbContext.Banners
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

            if (banner == null)
            {
                return NotFound(new { error = "Banner not found" });
            }

            // Cache the result in Redis
            await _cache.SetBannerAsync(banner, cancellationToken);

            var bannerResponse = resolve && !string.IsNullOrWhiteSpace(lang)
                ? BannerResponse.FromEntityForClient(banner, lang, effectivePlatform)
                : BannerResponse.FromEntity(banner);
            return Ok(bannerResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching banner {BannerId}", id);
            return StatusCode(500, new { error = "Failed to fetch banner" });
        }
    }

    /// <summary>
    /// Create a new banner
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateBannerAsync([FromBody] CreateBannerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { error = "Title is required" });
            }

            var banner = request.ToEntity();
            await _dbContext.Banners.AddAsync(banner, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created banner {BannerId}", banner.Id);

            // Invalidate cache after creating new banner
            await _cache.InvalidateBannerCacheAsync(cancellationToken);
            await PublishRevalidationAsync("created", cancellationToken);

            return CreatedAtAction(
                nameof(GetBannerAsync),
                new { id = banner.Id },
                BannerResponse.FromEntity(banner)
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating banner");
            return StatusCode(500, new { error = "Failed to create banner" });
        }
    }

    /// <summary>
    /// Update an existing banner
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBannerAsync(int id, [FromBody] UpdateBannerRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var banner = await _dbContext.Banners.FindAsync(new object[] { id }, cancellationToken);

            if (banner == null)
            {
                return NotFound(new { error = "Banner not found" });
            }

            if (request.Title != null) banner.Title = request.Title;
            if (request.Subtitle != null) banner.Subtitle = request.Subtitle;
            if (request.Description != null) banner.Description = request.Description;
            if (request.ImageUrl != null) banner.ImageUrl = request.ImageUrl;
            if (request.Lang != null) banner.Lang = request.Lang;
            if (request.Path != null) banner.Path = request.Path;
            if (request.IsActive.HasValue) banner.IsActive = request.IsActive.Value;

            // Update language-specific image URLs
            if (request.ImageUrls != null)
            {
                var existingUrls = banner.GetImageUrls();
                var newUrls = request.ImageUrls.ToDictionary();
                foreach (var (key, value) in newUrls)
                {
                    existingUrls[key] = value;
                }
                banner.SetImageUrls(existingUrls);
            }

            // Update language-specific deeplinks
            if (request.Deeplinks != null)
            {
                var existingLinks = banner.GetDeeplinks();
                var newLinks = request.Deeplinks.ToDictionary();
                foreach (var (key, value) in newLinks)
                {
                    existingLinks[key] = value;
                }
                banner.SetDeeplinks(existingLinks);
            }

            // Update platform-specific configurations
            if (request.Platforms != null)
            {
                var existingPlatforms = banner.GetPlatforms();
                var newPlatforms = request.Platforms.ToEntity();

                // Merge mobile config
                if (newPlatforms.Mobile != null)
                {
                    existingPlatforms.Mobile ??= new();
                    if (newPlatforms.Mobile.ImageUrls != null)
                    {
                        existingPlatforms.Mobile.ImageUrls ??= new();
                        foreach (var (key, value) in newPlatforms.Mobile.ImageUrls)
                        {
                            existingPlatforms.Mobile.ImageUrls[key] = value;
                        }
                    }
                    if (newPlatforms.Mobile.Deeplinks != null)
                    {
                        existingPlatforms.Mobile.Deeplinks ??= new();
                        foreach (var (key, value) in newPlatforms.Mobile.Deeplinks)
                        {
                            existingPlatforms.Mobile.Deeplinks[key] = value;
                        }
                    }
                }

                // Merge web config
                if (newPlatforms.Web != null)
                {
                    existingPlatforms.Web ??= new();
                    if (newPlatforms.Web.ImageUrls != null)
                    {
                        existingPlatforms.Web.ImageUrls ??= new();
                        foreach (var (key, value) in newPlatforms.Web.ImageUrls)
                        {
                            existingPlatforms.Web.ImageUrls[key] = value;
                        }
                    }
                    if (newPlatforms.Web.Deeplinks != null)
                    {
                        existingPlatforms.Web.Deeplinks ??= new();
                        foreach (var (key, value) in newPlatforms.Web.Deeplinks)
                        {
                            existingPlatforms.Web.Deeplinks[key] = value;
                        }
                    }
                }

                banner.SetPlatforms(existingPlatforms);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated banner {BannerId}", id);

            // Invalidate cache after updating banner
            await _cache.InvalidateBannerByIdAsync(id, cancellationToken);
            await PublishRevalidationAsync("updated", cancellationToken);

            return Ok(BannerResponse.FromEntity(banner));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating banner {BannerId}", id);
            return StatusCode(500, new { error = "Failed to update banner" });
        }
    }

    /// <summary>
    /// Delete a banner (soft delete by setting IsActive to false)
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBannerAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var banner = await _dbContext.Banners.FindAsync(new object[] { id }, cancellationToken);

            if (banner == null)
            {
                return NotFound(new { error = "Banner not found" });
            }

            banner.IsActive = false;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted (deactivated) banner {BannerId}", id);

            // Invalidate cache after deleting banner
            await _cache.InvalidateBannerByIdAsync(id, cancellationToken);
            await PublishRevalidationAsync("deleted", cancellationToken);

            return Ok(new { success = true, message = "Banner deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting banner {BannerId}", id);
            return StatusCode(500, new { error = "Failed to delete banner" });
        }
    }

    /// <summary>
    /// Permanently delete a banner from the database
    /// </summary>
    [HttpDelete("{id}/permanent")]
    public async Task<IActionResult> PermanentlyDeleteBannerAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var banner = await _dbContext.Banners.FindAsync(new object[] { id }, cancellationToken);

            if (banner == null)
            {
                return NotFound(new { error = "Banner not found" });
            }

            _dbContext.Banners.Remove(banner);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Permanently deleted banner {BannerId}", id);

            // Invalidate cache after permanently deleting banner
            await _cache.InvalidateBannerByIdAsync(id, cancellationToken);
            await PublishRevalidationAsync("deleted", cancellationToken);

            return Ok(new { success = true, message = "Banner permanently deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error permanently deleting banner {BannerId}", id);
            return StatusCode(500, new { error = "Failed to permanently delete banner" });
        }
    }

    /// <summary>
    /// Revalidate (refresh) banner caches
    /// </summary>
    [HttpPost("revalidate")]
    public async Task<IActionResult> RevalidateBannersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cache.InvalidateBannerCacheAsync(cancellationToken);

            var banners = await _dbContext.Banners
                .AsNoTracking()
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(cancellationToken);

            await _cache.SetBannersAsync(banners, null, cancellationToken);
            foreach (var group in banners.GroupBy(b => b.Lang))
            {
                await _cache.SetBannersAsync(group.ToList(), group.Key, cancellationToken);
            }

            await PublishRevalidationAsync("revalidated", cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Banners cache revalidated",
                count = banners.Count,
                timestamp = _timeProvider.GetUtcNow().ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating banners cache");
            return StatusCode(500, new { error = "Failed to revalidate cache" });
        }
    }

    private Task PublishRevalidationAsync(string changeType, CancellationToken cancellationToken)
    {
        var updatedAt = _timeProvider.GetUtcNow();
        var payload = new CacheRevalidationEvent(
            CacheResources.Banners,
            changeType,
            updatedAt.ToUnixTimeMilliseconds().ToString(),
            updatedAt);

        return _publisher.PublishAsync(payload, cancellationToken);
    }
}
