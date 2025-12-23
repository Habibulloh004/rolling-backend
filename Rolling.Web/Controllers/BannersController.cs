using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<BannersController> _logger;

    public BannersController(AppDbContext dbContext, IRedisBannerCache cache, ILogger<BannersController> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Get all active banners, sorted by creation date (newest first)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBannersAsync([FromQuery] string? lang = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try to get from Redis cache first
            var cachedBanners = await _cache.GetBannersAsync(lang, cancellationToken);
            if (cachedBanners != null)
            {
                var cachedResponse = new BannersListResponse
                {
                    Banners = cachedBanners.Select(BannerResponse.FromEntity).ToList()
                };
                return Ok(cachedResponse);
            }

            // Cache miss - fetch from database
            var query = _dbContext.Banners
                .AsNoTracking()
                .Where(b => b.IsActive);

            if (!string.IsNullOrWhiteSpace(lang))
            {
                query = query.Where(b => b.Lang == lang);
            }

            var banners = await query
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync(cancellationToken);

            // Cache the result in Redis
            await _cache.SetBannersAsync(banners, lang, cancellationToken);

            var response = new BannersListResponse
            {
                Banners = banners.Select(BannerResponse.FromEntity).ToList()
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
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBannerAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            // Try to get from Redis cache first
            var cachedBanner = await _cache.GetBannerByIdAsync(id, cancellationToken);
            if (cachedBanner != null)
            {
                return Ok(BannerResponse.FromEntity(cachedBanner));
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

            return Ok(BannerResponse.FromEntity(banner));
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

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated banner {BannerId}", id);

            // Invalidate cache after updating banner
            await _cache.InvalidateBannerByIdAsync(id, cancellationToken);

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

            return Ok(new { success = true, message = "Banner permanently deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error permanently deleting banner {BannerId}", id);
            return StatusCode(500, new { error = "Failed to permanently delete banner" });
        }
    }
}
