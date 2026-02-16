using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Persistence.Memory;
using Rolling.Infrastructure.Poster;
using Rolling.Web.Auth;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/cache")]
[AdminAuthorize]
public class CacheController : ControllerBase
{
    private readonly IInMemoryCacheService _cacheService;
    private readonly ICachedPosterService _posterCache;
    private readonly ILogger<CacheController> _logger;

    public CacheController(
        IInMemoryCacheService cacheService,
        ICachedPosterService posterCache,
        ILogger<CacheController> logger)
    {
        _cacheService = cacheService;
        _posterCache = posterCache;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/cache/stats
    /// Get cache statistics
    /// </summary>
    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        try
        {
            var stats = _cacheService.GetStats();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache stats");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/cache/clear
    /// Clear all cache
    /// </summary>
    [HttpPost("clear")]
    public IActionResult ClearCache()
    {
        try
        {
            var count = _cacheService.Clear();
            return Ok(new
            {
                message = "Cache cleared",
                keysDeleted = count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/cache/:key
    /// Delete specific cache key
    /// </summary>
    [HttpDelete("{key}")]
    public IActionResult DeleteKey(string key)
    {
        try
        {
            _cacheService.Delete(key);
            return Ok(new
            {
                message = $"Cache key \"{key}\" deleted"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cache key {Key}", key);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/cache/:key
    /// Get specific cache key value
    /// </summary>
    [HttpGet("{key}")]
    public IActionResult GetKey(string key)
    {
        try
        {
            var data = _cacheService.Get<object>(key);
            var ttl = _cacheService.GetTTL(key);

            if (data == null)
            {
                return NotFound(new { error = "Cache key not found" });
            }

            return Ok(new
            {
                key,
                data,
                ttl = ttl?.TotalSeconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache key {Key}", key);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/cache/:key
    /// Set cache key manually
    /// </summary>
    [HttpPost("{key}")]
    public IActionResult SetKey(string key, [FromBody] SetCacheRequest request)
    {
        try
        {
            if (request.Data == null)
            {
                return BadRequest(new { error = "Data is required" });
            }

            TimeSpan? ttl = request.Ttl.HasValue
                ? TimeSpan.FromSeconds(request.Ttl.Value)
                : null;

            _cacheService.Set(key, request.Data, ttl);

            return Ok(new
            {
                message = $"Cache key \"{key}\" set successfully",
                key,
                ttl = request.Ttl?.ToString() ?? "default"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key {Key}", key);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    public class SetCacheRequest
    {
        public object? Data { get; set; }
        public int? Ttl { get; set; }
    }

    /// <summary>
    /// POST /api/cache/revalidate/:resource
    /// Revalidate poster array caches
    /// </summary>
    [HttpPost("revalidate/{resource}")]
    public async Task<IActionResult> RevalidateResourceAsync(string resource, CancellationToken cancellationToken)
    {
        try
        {
            switch (resource.ToLowerInvariant())
            {
                case "products":
                    return Ok(await _posterCache.RevalidateAllProductsAsync(cancellationToken));
                case "categories":
                    return Ok(await _posterCache.RevalidateCategoriesAsync(cancellationToken));
                case "promotions":
                    return Ok(await _posterCache.RevalidatePromotionsAsync(null, cancellationToken));
                case "client-groups":
                case "clientgroups":
                    return Ok(await _posterCache.RevalidateClientGroupsAsync(cancellationToken));
                case "spots":
                    return Ok(await _posterCache.RevalidateSpotsAsync(cancellationToken));
                case "employees":
                    return Ok(await _posterCache.RevalidateEmployeesAsync(cancellationToken));
                case "clients":
                    return BadRequest(new { error = "Client revalidation is disabled." });
                case "transactions":
                    return BadRequest(new { error = "Transactions require query parameters; use /api/poster/transactions/revalidate." });
                default:
                    return NotFound(new { error = $"Unknown resource '{resource}'" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revalidating resource {Resource}", resource);
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
