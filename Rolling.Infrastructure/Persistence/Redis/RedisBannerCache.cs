using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using StackExchange.Redis;

namespace Rolling.Infrastructure.Persistence.Redis;

public interface IRedisBannerCache
{
    Task<List<Banner>?> GetBannersAsync(string? lang = null, CancellationToken cancellationToken = default);
    Task<Banner?> GetBannerByIdAsync(int id, CancellationToken cancellationToken = default);
    Task SetBannersAsync(List<Banner> banners, string? lang = null, CancellationToken cancellationToken = default);
    Task SetBannerAsync(Banner banner, CancellationToken cancellationToken = default);
    Task InvalidateBannerCacheAsync(CancellationToken cancellationToken = default);
    Task InvalidateBannerByIdAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class RedisBannerCache : IRedisBannerCache
{
    private const string BannersKeyPrefix = "banners:";
    private const string BannersAllKey = "banners:all";
    private const string BannerByIdPrefix = "banner:id:";

    // Cache indefinitely - only invalidate on DB changes (create/update/delete)
    private static readonly TimeSpan? DefaultExpiration = null;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisBannerCache> _logger;

    public RedisBannerCache(IConnectionMultiplexer redis, ILogger<RedisBannerCache> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<List<Banner>?> GetBannersAsync(string? lang = null, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = string.IsNullOrWhiteSpace(lang) ? BannersAllKey : $"{BannersKeyPrefix}lang:{lang}";

            var cached = await db.StringGetAsync(key);
            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("🔍 Cache MISS: Banners (lang: {Lang})", lang ?? "all");
                return null;
            }

            var banners = JsonSerializer.Deserialize<List<BannerDocument>>(cached.ToString(), SerializerOptions);
            if (banners == null)
            {
                _logger.LogWarning("Failed to deserialize cached banners");
                return null;
            }

            _logger.LogInformation("✅ Cache HIT: Banners (lang: {Lang}, count: {Count})", lang ?? "all", banners.Count);
            return banners.Select(d => d.ToEntity()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting banners from Redis cache (lang: {Lang})", lang);
            return null;
        }
    }

    public async Task<Banner?> GetBannerByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = $"{BannerByIdPrefix}{id}";

            var cached = await db.StringGetAsync(key);
            if (cached.IsNullOrEmpty)
            {
                _logger.LogInformation("🔍 Cache MISS: Banner ID {BannerId}", id);
                return null;
            }

            var document = JsonSerializer.Deserialize<BannerDocument>(cached.ToString(), SerializerOptions);
            if (document == null)
            {
                _logger.LogWarning("Failed to deserialize cached banner {BannerId}", id);
                return null;
            }

            _logger.LogInformation("✅ Cache HIT: Banner ID {BannerId}", id);
            return document.ToEntity();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting banner {BannerId} from Redis cache", id);
            return null;
        }
    }

    public async Task SetBannersAsync(List<Banner> banners, string? lang = null, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = string.IsNullOrWhiteSpace(lang) ? BannersAllKey : $"{BannersKeyPrefix}lang:{lang}";

            var documents = banners.Select(BannerDocument.FromEntity).ToList();
            var json = JsonSerializer.Serialize(documents, SerializerOptions);

            await db.StringSetAsync(key, json, DefaultExpiration);
            _logger.LogInformation("💾 Cached: Banners (lang: {Lang}, count: {Count}, TTL: infinite - invalidated on DB changes only)",
                lang ?? "all", banners.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching banners to Redis (lang: {Lang})", lang);
        }
    }

    public async Task SetBannerAsync(Banner banner, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = $"{BannerByIdPrefix}{banner.Id}";

            var document = BannerDocument.FromEntity(banner);
            var json = JsonSerializer.Serialize(document, SerializerOptions);

            await db.StringSetAsync(key, json, DefaultExpiration);
            _logger.LogInformation("💾 Cached: Banner ID {BannerId} (TTL: infinite - invalidated on DB changes only)", banner.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching banner {BannerId} to Redis", banner.Id);
        }
    }

    public async Task InvalidateBannerCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());

            // Delete all banner-related keys
            var keys = server.Keys(pattern: $"{BannersKeyPrefix}*").ToArray();
            var allKey = new RedisKey(BannersAllKey);

            var batch = db.CreateBatch();
            var tasks = new List<Task>();

            // Delete all banners keys
            tasks.Add(batch.KeyDeleteAsync(allKey));

            // Delete language-specific keys
            foreach (var key in keys)
            {
                tasks.Add(batch.KeyDeleteAsync(key));
            }

            batch.Execute();
            await Task.WhenAll(tasks);

            _logger.LogInformation("🗑️  Invalidated all banner cache keys (count: {Count})", keys.Length + 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating banner cache");
        }
    }

    public async Task InvalidateBannerByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var db = _redis.GetDatabase();
            var key = $"{BannerByIdPrefix}{id}";

            await db.KeyDeleteAsync(key);
            _logger.LogInformation("🗑️  Invalidated cache: Banner ID {BannerId}", id);

            // Also invalidate list caches since they contain this banner
            await InvalidateBannerCacheAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating banner {BannerId} cache", id);
        }
    }

    // Document class for serialization
    private sealed record BannerDocument(
        int Id,
        string Title,
        string? Subtitle,
        string Description,
        string? ImageUrl,
        string Lang,
        string? Path,
        DateTime CreatedAt,
        bool IsActive,
        string ImageUrls,
        string Deeplinks,
        string Platforms)
    {
        public static BannerDocument FromEntity(Banner banner) => new(
            banner.Id,
            banner.Title,
            banner.Subtitle,
            banner.Description,
            banner.ImageUrl,
            banner.Lang,
            banner.Path,
            banner.CreatedAt,
            banner.IsActive,
            banner.ImageUrls,
            banner.Deeplinks,
            banner.Platforms
        );

        public Banner ToEntity() => new()
        {
            Id = Id,
            Title = Title,
            Subtitle = Subtitle,
            Description = Description,
            ImageUrl = ImageUrl,
            Lang = Lang,
            Path = Path,
            CreatedAt = CreatedAt,
            IsActive = IsActive,
            ImageUrls = ImageUrls ?? "{}",
            Deeplinks = Deeplinks ?? "{}",
            Platforms = Platforms ?? "{}"
        };
    }
}
