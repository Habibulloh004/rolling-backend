using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Cache;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Infrastructure.Persistence.Redis;
using Rolling.Web.Auth;
using Rolling.Web.Models.BranchConfigs;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/branches")]
public sealed class BranchConfigurationsController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly IRedisBranchConfigCache _cache;
    private readonly ICacheRevalidationPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BranchConfigurationsController> _logger;

    public BranchConfigurationsController(
        AppDbContext dbContext,
        IRedisBranchConfigCache cache,
        ICacheRevalidationPublisher publisher,
        TimeProvider timeProvider,
        ILogger<BranchConfigurationsController> logger)
    {
        _dbContext = dbContext;
        _cache = cache;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Get all active branch configurations, sorted by sort order
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetBranchConfigurationsAsync(
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Skip cache if refresh requested
            if (!refresh)
            {
                var cachedConfigs = await _cache.GetBranchConfigurationsAsync(cancellationToken);
                if (cachedConfigs != null)
                {
                    var cachedResponse = new BranchConfigurationsListResponse
                    {
                        Data = cachedConfigs.Select(BranchConfigurationResponse.FromEntity).ToList(),
                        Source = "cache",
                        Cached = true,
                        LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    };
                    return Ok(cachedResponse);
                }
            }

            // Cache miss or refresh - fetch from database
            var configs = await _dbContext.BranchConfigurations
                .AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.NameEn)
                .ToListAsync(cancellationToken);

            // Cache the result in Redis
            await _cache.SetBranchConfigurationsAsync(configs, cancellationToken);

            var response = new BranchConfigurationsListResponse
            {
                Data = configs.Select(BranchConfigurationResponse.FromEntity).ToList(),
                Source = "database",
                Cached = false,
                LastUpdated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching branch configurations");
            return StatusCode(500, new { error = "Failed to fetch branch configurations" });
        }
    }

    /// <summary>
    /// Get a specific branch configuration by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBranchConfigurationAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            // Try to get from Redis cache first
            var cachedConfig = await _cache.GetBranchConfigurationByIdAsync(id, cancellationToken);
            if (cachedConfig != null)
            {
                return Ok(new SingleBranchConfigurationResponse
                {
                    Data = BranchConfigurationResponse.FromEntity(cachedConfig),
                    Source = "cache",
                    Cached = true
                });
            }

            // Cache miss - fetch from database
            var config = await _dbContext.BranchConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

            if (config == null)
            {
                return NotFound(new { error = "Branch configuration not found" });
            }

            // Cache the result in Redis
            await _cache.SetBranchConfigurationAsync(config, cancellationToken);

            return Ok(new SingleBranchConfigurationResponse
            {
                Data = BranchConfigurationResponse.FromEntity(config),
                Source = "database",
                Cached = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching branch configuration {Id}", id);
            return StatusCode(500, new { error = "Failed to fetch branch configuration" });
        }
    }

    /// <summary>
    /// Get a specific branch configuration by Poster spot ID
    /// </summary>
    [HttpGet("spot/{spotId}")]
    public async Task<IActionResult> GetBranchConfigurationBySpotIdAsync(int spotId, CancellationToken cancellationToken)
    {
        try
        {
            // Try to get from Redis cache first
            var cachedConfig = await _cache.GetBranchConfigurationBySpotIdAsync(spotId, cancellationToken);
            if (cachedConfig != null)
            {
                return Ok(new SingleBranchConfigurationResponse
                {
                    Data = BranchConfigurationResponse.FromEntity(cachedConfig),
                    Source = "cache",
                    Cached = true
                });
            }

            // Cache miss - fetch from database
            var config = await _dbContext.BranchConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.SpotId == spotId, cancellationToken);

            if (config == null)
            {
                return NotFound(new { error = "Branch configuration not found for spot ID" });
            }

            // Cache the result in Redis
            await _cache.SetBranchConfigurationAsync(config, cancellationToken);

            return Ok(new SingleBranchConfigurationResponse
            {
                Data = BranchConfigurationResponse.FromEntity(config),
                Source = "database",
                Cached = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching branch configuration for spot {SpotId}", spotId);
            return StatusCode(500, new { error = "Failed to fetch branch configuration" });
        }
    }

    /// <summary>
    /// Create a new branch configuration
    /// </summary>
    [AdminAuthorize]
    [HttpPost]
    public async Task<IActionResult> CreateBranchConfigurationAsync(
        [FromBody] CreateBranchConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.NameEn))
            {
                return BadRequest(new { error = "Name (English) is required" });
            }

            if (request.SpotId <= 0)
            {
                return BadRequest(new { error = "Valid SpotId is required" });
            }

            // Check for duplicate SpotId
            var existing = await _dbContext.BranchConfigurations
                .AnyAsync(b => b.SpotId == request.SpotId, cancellationToken);

            if (existing)
            {
                return BadRequest(new { error = $"Branch configuration with SpotId {request.SpotId} already exists" });
            }

            var config = request.ToEntity();
            await _dbContext.BranchConfigurations.AddAsync(config, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created branch configuration {Id} for spot {SpotId}", config.Id, config.SpotId);

            // Invalidate cache after creating new config
            await _cache.InvalidateBranchConfigCacheAsync(cancellationToken);
            await PublishRevalidationAsync("created", config, cancellationToken);

            return CreatedAtAction(
                nameof(GetBranchConfigurationAsync),
                new { id = config.Id },
                new SingleBranchConfigurationResponse
                {
                    Data = BranchConfigurationResponse.FromEntity(config),
                    Source = "database",
                    Cached = false
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating branch configuration");
            return StatusCode(500, new { error = "Failed to create branch configuration" });
        }
    }

    /// <summary>
    /// Update an existing branch configuration
    /// </summary>
    [AdminAuthorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateBranchConfigurationAsync(
        int id,
        [FromBody] UpdateBranchConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var config = await _dbContext.BranchConfigurations.FindAsync(new object[] { id }, cancellationToken);

            if (config == null)
            {
                return NotFound(new { error = "Branch configuration not found" });
            }

            // Update only provided fields
            if (request.SpotId.HasValue) config.SpotId = request.SpotId.Value;
            if (request.NameEn != null) config.NameEn = request.NameEn;
            if (request.NameRu != null) config.NameRu = request.NameRu;
            if (request.NameUz != null) config.NameUz = request.NameUz;
            if (request.ShortDescriptionEn != null) config.ShortDescriptionEn = request.ShortDescriptionEn;
            if (request.ShortDescriptionRu != null) config.ShortDescriptionRu = request.ShortDescriptionRu;
            if (request.ShortDescriptionUz != null) config.ShortDescriptionUz = request.ShortDescriptionUz;
            if (request.IsActive.HasValue) config.IsActive = request.IsActive.Value;
            if (request.IsTemporarilyClosed.HasValue) config.IsTemporarilyClosed = request.IsTemporarilyClosed.Value;
            if (request.ClosureMessageEn != null) config.ClosureMessageEn = request.ClosureMessageEn;
            if (request.ClosureMessageRu != null) config.ClosureMessageRu = request.ClosureMessageRu;
            if (request.ClosureMessageUz != null) config.ClosureMessageUz = request.ClosureMessageUz;
            if (request.OpenTime != null) config.OpenTime = request.OpenTime;
            if (request.CloseTime != null) config.CloseTime = request.CloseTime;
            if (request.Timezone != null) config.Timezone = request.Timezone;
            if (request.Is24Hours.HasValue) config.Is24Hours = request.Is24Hours.Value;
            if (request.DeliveryFee.HasValue) config.DeliveryFee = request.DeliveryFee.Value;
            if (request.FreeDeliveryThreshold.HasValue) config.FreeDeliveryThreshold = request.FreeDeliveryThreshold.Value;
            if (request.MinOrderAmount.HasValue) config.MinOrderAmount = request.MinOrderAmount.Value;
            if (request.MaxOrderAmount.HasValue) config.MaxOrderAmount = request.MaxOrderAmount.Value;
            if (request.EtaMinMinutes.HasValue) config.EtaMinMinutes = request.EtaMinMinutes.Value;
            if (request.EtaMaxMinutes.HasValue) config.EtaMaxMinutes = request.EtaMaxMinutes.Value;
            if (request.EtaDisplayTextEn != null) config.EtaDisplayTextEn = request.EtaDisplayTextEn;
            if (request.EtaDisplayTextRu != null) config.EtaDisplayTextRu = request.EtaDisplayTextRu;
            if (request.EtaDisplayTextUz != null) config.EtaDisplayTextUz = request.EtaDisplayTextUz;
            if (request.DeliveryRadius.HasValue) config.DeliveryRadius = request.DeliveryRadius.Value;
            if (request.IsDeliveryEnabled.HasValue) config.IsDeliveryEnabled = request.IsDeliveryEnabled.Value;
            if (request.IsPickupEnabled.HasValue) config.IsPickupEnabled = request.IsPickupEnabled.Value;
            if (request.PickupEtaMinutes.HasValue) config.PickupEtaMinutes = request.PickupEtaMinutes.Value;
            if (request.PickupDiscountPercentage.HasValue) config.PickupDiscountPercentage = request.PickupDiscountPercentage.Value;
            if (request.Phone != null) config.Phone = request.Phone;
            if (request.AlternatePhone != null) config.AlternatePhone = request.AlternatePhone;
            if (request.Email != null) config.Email = request.Email;
            if (request.Whatsapp != null) config.Whatsapp = request.Whatsapp;
            if (request.Telegram != null) config.Telegram = request.Telegram;
            if (request.Instagram != null) config.Instagram = request.Instagram;
            if (request.Latitude.HasValue) config.Latitude = request.Latitude.Value;
            if (request.Longitude.HasValue) config.Longitude = request.Longitude.Value;
            if (request.AddressEn != null) config.AddressEn = request.AddressEn;
            if (request.AddressRu != null) config.AddressRu = request.AddressRu;
            if (request.AddressUz != null) config.AddressUz = request.AddressUz;
            if (request.CityEn != null) config.CityEn = request.CityEn;
            if (request.CityRu != null) config.CityRu = request.CityRu;
            if (request.CityUz != null) config.CityUz = request.CityUz;
            if (request.DistrictEn != null) config.DistrictEn = request.DistrictEn;
            if (request.DistrictRu != null) config.DistrictRu = request.DistrictRu;
            if (request.DistrictUz != null) config.DistrictUz = request.DistrictUz;
            if (request.GoogleMapsUrl != null) config.GoogleMapsUrl = request.GoogleMapsUrl;
            if (request.YandexMapsUrl != null) config.YandexMapsUrl = request.YandexMapsUrl;
            if (request.AcceptsCash.HasValue) config.AcceptsCash = request.AcceptsCash.Value;
            if (request.AcceptsCard.HasValue) config.AcceptsCard = request.AcceptsCard.Value;
            if (request.AcceptsOnlinePayment.HasValue) config.AcceptsOnlinePayment = request.AcceptsOnlinePayment.Value;
            if (request.AcceptsClick.HasValue) config.AcceptsClick = request.AcceptsClick.Value;
            if (request.AcceptsPayme.HasValue) config.AcceptsPayme = request.AcceptsPayme.Value;
            if (request.AcceptsUzum.HasValue) config.AcceptsUzum = request.AcceptsUzum.Value;
            if (request.IsDineInEnabled.HasValue) config.IsDineInEnabled = request.IsDineInEnabled.Value;
            if (request.IsReservationEnabled.HasValue) config.IsReservationEnabled = request.IsReservationEnabled.Value;
            if (request.IsLoyaltyEnabled.HasValue) config.IsLoyaltyEnabled = request.IsLoyaltyEnabled.Value;
            if (request.IsPromoCodesEnabled.HasValue) config.IsPromoCodesEnabled = request.IsPromoCodesEnabled.Value;
            if (request.LogoUrl != null) config.LogoUrl = request.LogoUrl;
            if (request.BannerUrl != null) config.BannerUrl = request.BannerUrl;
            if (request.ThumbnailUrl != null) config.ThumbnailUrl = request.ThumbnailUrl;
            if (request.SortOrder.HasValue) config.SortOrder = request.SortOrder.Value;
            if (request.Tags != null) config.TagsJson = JsonSerializer.Serialize(request.Tags);

            config.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated branch configuration {Id}", id);

            // Invalidate all cache and re-cache the updated branch
            await _cache.InvalidateBranchConfigCacheAsync(cancellationToken);
            await _cache.SetBranchConfigurationAsync(config, cancellationToken);
            await PublishRevalidationAsync("updated", config, cancellationToken);

            return Ok(new SingleBranchConfigurationResponse
            {
                Data = BranchConfigurationResponse.FromEntity(config),
                Source = "database",
                Cached = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating branch configuration {Id}", id);
            return StatusCode(500, new { error = "Failed to update branch configuration" });
        }
    }

    /// <summary>
    /// Delete a branch configuration (soft delete by setting IsActive to false)
    /// </summary>
    [AdminAuthorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBranchConfigurationAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _dbContext.BranchConfigurations.FindAsync(new object[] { id }, cancellationToken);

            if (config == null)
            {
                return NotFound(new { error = "Branch configuration not found" });
            }

            config.IsActive = false;
            config.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted (deactivated) branch configuration {Id}", id);

            // Invalidate both single branch cache AND list cache
            await _cache.InvalidateBranchConfigByIdAsync(id, cancellationToken);
            await _cache.InvalidateBranchConfigCacheAsync(cancellationToken);
            await PublishRevalidationAsync("deleted", config, cancellationToken);

            return Ok(new { success = true, message = "Branch configuration deleted" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting branch configuration {Id}", id);
            return StatusCode(500, new { error = "Failed to delete branch configuration" });
        }
    }

    /// <summary>
    /// Revalidate (refresh) all branch configuration caches
    /// </summary>
    [HttpPost("revalidate")]
    public async Task<IActionResult> RevalidateBranchConfigurationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cache.InvalidateBranchConfigCacheAsync(cancellationToken);

            // Fetch fresh data from database
            var configs = await _dbContext.BranchConfigurations
                .AsNoTracking()
                .Where(b => b.IsActive)
                .OrderBy(b => b.SortOrder)
                .ThenBy(b => b.NameEn)
                .ToListAsync(cancellationToken);

            // Recache
            await _cache.SetBranchConfigurationsAsync(configs, cancellationToken);

            _logger.LogInformation("Revalidated branch configurations cache (count: {Count})", configs.Count);
            await PublishRevalidationAsync("revalidated", null, cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Branch configurations cache revalidated",
                count = configs.Count,
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating branch configurations cache");
            return StatusCode(500, new { error = "Failed to revalidate cache" });
        }
    }

    /// <summary>
    /// Revalidate a specific branch configuration cache
    /// </summary>
    [HttpPost("{id}/revalidate")]
    public async Task<IActionResult> RevalidateBranchConfigurationAsync(int id, CancellationToken cancellationToken)
    {
        try
        {
            var config = await _dbContext.BranchConfigurations
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

            if (config == null)
            {
                return NotFound(new { error = "Branch configuration not found" });
            }

            await _cache.InvalidateBranchConfigByIdAsync(id, cancellationToken);
            await _cache.SetBranchConfigurationAsync(config, cancellationToken);

            _logger.LogInformation("Revalidated branch configuration {Id} cache", id);
            await PublishRevalidationAsync("revalidated", config, cancellationToken);

            return Ok(new
            {
                success = true,
                message = $"Branch configuration {id} cache revalidated",
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating branch configuration {Id} cache", id);
            return StatusCode(500, new { error = "Failed to revalidate cache" });
        }
    }

    private Task PublishRevalidationAsync(string changeType, BranchConfiguration? config, CancellationToken cancellationToken)
    {
        var updatedAt = config != null
            ? new DateTimeOffset(DateTime.SpecifyKind(config.UpdatedAt, DateTimeKind.Utc))
            : _timeProvider.GetUtcNow();

        var scope = config != null
            ? new CacheRevalidationScope(BranchId: config.Id.ToString())
            : null;

        var payload = new CacheRevalidationEvent(
            CacheResources.Branches,
            changeType,
            updatedAt.ToUnixTimeMilliseconds().ToString(),
            updatedAt,
            scope);

        return _publisher.PublishAsync(payload, cancellationToken);
    }
}
