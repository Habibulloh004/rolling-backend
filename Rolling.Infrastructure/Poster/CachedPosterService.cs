using System.Globalization;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Infrastructure.Cache;
using Rolling.Infrastructure.Persistence.Memory;

namespace Rolling.Infrastructure.Poster;

public interface ICachedPosterService
{
    Task<CachedResponse<JsonDocument?>> GetProductsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetPromotionsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetClientsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetClientAsync(string clientId, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetClientGroupsAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetTransactionsAsync(Dictionary<string, string?> queryParams, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetTransactionProductsAsync(string transactionId, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetSpotsAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetEmployeesAsync(CancellationToken cancellationToken = default);

    Task<JsonDocument?> CreateClientAsync(JsonElement payload, CancellationToken cancellationToken = default);
    Task<JsonDocument?> CreateIncomingOrderAsync(JsonElement payload, CancellationToken cancellationToken = default);

    Task<CachedResponse<JsonDocument?>> RevalidateProductsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateAllProductsAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateCategoriesAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidatePromotionsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateClientsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateClientAsync(string clientId, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateClientGroupsAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateTransactionsAsync(Dictionary<string, string?> queryParams, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateSpotsAsync(CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> RevalidateEmployeesAsync(CancellationToken cancellationToken = default);
    Task<RevalidateAllResponse> RevalidateAllAsync(CancellationToken cancellationToken = default);
}

public class CachedResponse<T>
{
    public T Data { get; set; } = default!;
    public string Source { get; set; } = "api";
    public bool Cached { get; set; } = false;
    public string? Version { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

public class RevalidateAllResponse
{
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class CachedPosterService : ICachedPosterService
{
    private readonly PosterService _posterService;
    private readonly PosterArrayCacheStore _cacheStore;
    private readonly ICacheRevalidationPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CachedPosterService> _logger;

    private readonly SemaphoreSlim _productsLock = new(1, 1);
    private readonly SemaphoreSlim _categoriesLock = new(1, 1);
    private readonly SemaphoreSlim _promotionsLock = new(1, 1);
    private readonly SemaphoreSlim _clientGroupsLock = new(1, 1);
    private readonly SemaphoreSlim _spotsLock = new(1, 1);
    private readonly SemaphoreSlim _employeesLock = new(1, 1);
    private long _eventVersionCounter;

    public CachedPosterService(
        PosterService posterService,
        PosterArrayCacheStore cacheStore,
        ICacheRevalidationPublisher publisher,
        TimeProvider timeProvider,
        ILogger<CachedPosterService> logger)
    {
        _posterService = posterService;
        _cacheStore = cacheStore;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private static string BuildQueryKey(Dictionary<string, string?>? queryParams)
    {
        if (queryParams == null || queryParams.Count == 0)
            return "{}";

        return JsonSerializer.Serialize(queryParams);
    }

    private CachedResponse<JsonDocument?> BuildCacheResponse(ArrayCacheEntry<JsonDocument?> entry, string source, bool cached)
    {
        return new CachedResponse<JsonDocument?>
        {
            Data = entry.Data,
            Source = source,
            Cached = cached,
            Version = entry.Version.ToString(CultureInfo.InvariantCulture),
            UpdatedAt = entry.UpdatedAt
        };
    }

    private CachedResponse<JsonDocument?> BuildApiResponse(JsonDocument? data)
    {
        var version = Interlocked.Increment(ref _eventVersionCounter);
        var updatedAt = _timeProvider.GetUtcNow();

        return new CachedResponse<JsonDocument?>
        {
            Data = data,
            Source = "api",
            Cached = false,
            Version = version.ToString(CultureInfo.InvariantCulture),
            UpdatedAt = updatedAt
        };
    }

    private async Task PublishRevalidationAsync(
        string resource,
        ArrayCacheEntry<JsonDocument?> entry,
        string? changeType,
        CancellationToken cancellationToken)
    {
        var payload = new CacheRevalidationEvent(
            resource,
            changeType,
            entry.Version.ToString(CultureInfo.InvariantCulture),
            entry.UpdatedAt);

        await _publisher.PublishAsync(payload, cancellationToken);
    }

    private async Task PublishRevalidationAsync(
        string resource,
        string? changeType,
        CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _eventVersionCounter);
        var updatedAt = _timeProvider.GetUtcNow();
        var payload = new CacheRevalidationEvent(
            resource,
            changeType,
            version.ToString(CultureInfo.InvariantCulture),
            updatedAt);

        await _publisher.PublishAsync(payload, cancellationToken);
    }

    private async Task<CachedResponse<JsonDocument?>> RefreshWithLockAsync(
        SemaphoreSlim gate,
        KeyedInMemoryArrayCache<JsonDocument?> cache,
        string cacheKey,
        string resource,
        Func<Task<JsonDocument?>> fetch,
        ArrayCacheEntry<JsonDocument?>? fallbackEntry,
        string? changeType,
        CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var data = await fetch();
            if (data == null)
            {
                _logger.LogWarning("Poster refresh returned null for {Resource}", resource);
                if (fallbackEntry != null)
                {
                    return BuildCacheResponse(fallbackEntry, "memory", true);
                }

                return BuildApiResponse(null);
            }

            var entry = cache.Set(cacheKey, data);
            await PublishRevalidationAsync(resource, entry, changeType, cancellationToken);
            return BuildCacheResponse(entry, "api", false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static List<string> GetKnownKeys(KeyedInMemoryArrayCache<JsonDocument?> cache, string defaultKey)
    {
        var keys = cache.Keys.ToList();
        if (keys.Count == 0)
        {
            keys.Add(defaultKey);
            return keys;
        }

        if (!keys.Contains(defaultKey, StringComparer.Ordinal))
        {
            keys.Add(defaultKey);
        }

        return keys;
    }

    // ===== MENU ENDPOINTS =====

    public async Task<CachedResponse<JsonDocument?>> GetProductsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildQueryKey(queryParams);
        var cached = _cacheStore.Products.Get(cacheKey);
        if (cached != null)
        {
            return BuildCacheResponse(cached, "memory", true);
        }

        _logger.LogInformation("Poster products cache miss; refreshing...");
        return await RefreshWithLockAsync(
            _productsLock,
            _cacheStore.Products,
            cacheKey,
            CacheResources.Products,
            () => _posterService.GetProductsAsync(cancellationToken),
            null,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cacheStore.Categories.Get(PosterArrayCacheStore.DefaultKey);
        if (cached != null)
        {
            return BuildCacheResponse(cached, "memory", true);
        }

        _logger.LogInformation("Poster categories cache miss; refreshing...");
        return await RefreshWithLockAsync(
            _categoriesLock,
            _cacheStore.Categories,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.Categories,
            () => _posterService.GetCategoriesAsync(cancellationToken),
            null,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> GetPromotionsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildQueryKey(queryParams);
        var cached = _cacheStore.Promotions.Get(cacheKey);
        if (cached != null)
        {
            return BuildCacheResponse(cached, "memory", true);
        }

        _logger.LogInformation("Poster promotions cache miss; refreshing...");
        return await RefreshWithLockAsync(
            _promotionsLock,
            _cacheStore.Promotions,
            cacheKey,
            CacheResources.Promotions,
            () => _posterService.GetPromotionsAsync(queryParams, cancellationToken),
            null,
            "revalidated",
            cancellationToken);
    }

    // ===== CLIENT ENDPOINTS =====

    public async Task<CachedResponse<JsonDocument?>> GetClientsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching Poster clients without server-side caching");
        var data = await _posterService.GetClientsAsync(queryParams?.GetValueOrDefault("phone"), cancellationToken);
        return BuildApiResponse(data);
    }

    public async Task<CachedResponse<JsonDocument?>> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching Poster client {ClientId} without server-side caching", clientId);
        var data = await _posterService.GetClientAsync(clientId, cancellationToken);
        return BuildApiResponse(data);
    }

    public async Task<CachedResponse<JsonDocument?>> GetClientGroupsAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cacheStore.ClientGroups.Get(PosterArrayCacheStore.DefaultKey);
        if (cached != null)
        {
            return BuildCacheResponse(cached, "memory", true);
        }

        _logger.LogInformation("Poster client groups cache miss; refreshing...");
        return await RefreshWithLockAsync(
            _clientGroupsLock,
            _cacheStore.ClientGroups,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.ClientGroups,
            () => _posterService.GetClientGroupsAsync(cancellationToken),
            null,
            "revalidated",
            cancellationToken);
    }

    public async Task<JsonDocument?> CreateClientAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var result = await _posterService.CreateClientAsync(payload, cancellationToken);
        await PublishRevalidationAsync(CacheResources.Clients, "created", cancellationToken);
        return result;
    }

    // ===== ORDER ENDPOINTS =====

    public Task<JsonDocument?> CreateIncomingOrderAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        return _posterService.CreateIncomingOrderAsync(payload, cancellationToken);
    }

    // ===== TRANSACTION ENDPOINTS =====

    public async Task<CachedResponse<JsonDocument?>> GetTransactionsAsync(
        Dictionary<string, string?> queryParams,
        CancellationToken cancellationToken = default)
    {
        var courierId = queryParams.GetValueOrDefault("courier_id") ?? string.Empty;
        var dateFrom = queryParams.GetValueOrDefault("dateFrom") ?? string.Empty;
        var dateTo = queryParams.GetValueOrDefault("dateTo") ?? string.Empty;

        _logger.LogInformation("Fetching Poster transactions without server-side caching");
        var data = await _posterService.GetTransactionsAsync(courierId, dateFrom, dateTo, cancellationToken);
        return BuildApiResponse(data);
    }

    public async Task<CachedResponse<JsonDocument?>> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching Poster transaction {TransactionId} without server-side caching", transactionId);
        var data = await _posterService.GetTransactionAsync(transactionId, cancellationToken);
        return BuildApiResponse(data);
    }

    public async Task<CachedResponse<JsonDocument?>> GetTransactionProductsAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching Poster transaction products {TransactionId} without server-side caching", transactionId);
        var data = await _posterService.GetTransactionProductsAsync(transactionId, cancellationToken);
        return BuildApiResponse(data);
    }

    // ===== LOCATION ENDPOINTS =====

    public async Task<CachedResponse<JsonDocument?>> GetSpotsAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cacheStore.Spots.Get(PosterArrayCacheStore.DefaultKey);
        if (cached != null)
        {
            return BuildCacheResponse(cached, "memory", true);
        }

        _logger.LogInformation("Poster spots cache miss; refreshing...");
        return await RefreshWithLockAsync(
            _spotsLock,
            _cacheStore.Spots,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.Spots,
            () => _posterService.GetSpotsAsync(cancellationToken),
            null,
            "revalidated",
            cancellationToken);
    }

    // ===== EMPLOYEE ENDPOINTS =====

    public async Task<CachedResponse<JsonDocument?>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var cached = _cacheStore.Employees.Get(PosterArrayCacheStore.DefaultKey);
        if (cached != null)
        {
            return BuildCacheResponse(cached, "memory", true);
        }

        _logger.LogInformation("Poster employees cache miss; refreshing...");
        return await RefreshWithLockAsync(
            _employeesLock,
            _cacheStore.Employees,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.Employees,
            () => _posterService.GetEmployeesAsync(cancellationToken),
            null,
            "revalidated",
            cancellationToken);
    }

    // ===== REVALIDATION METHODS =====

    public async Task<CachedResponse<JsonDocument?>> RevalidateProductsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = BuildQueryKey(queryParams);
        var existing = _cacheStore.Products.Get(cacheKey);
        _logger.LogInformation("Revalidating products cache ({CacheKey})...", cacheKey);
        return await RefreshWithLockAsync(
            _productsLock,
            _cacheStore.Products,
            cacheKey,
            CacheResources.Products,
            () => _posterService.GetProductsAsync(cancellationToken),
            existing,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateAllProductsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Revalidating all products caches...");
        var keys = GetKnownKeys(_cacheStore.Products, BuildQueryKey(null));
        CachedResponse<JsonDocument?>? last = null;

        foreach (var key in keys)
        {
            var existing = _cacheStore.Products.Get(key);
            last = await RefreshWithLockAsync(
                _productsLock,
                _cacheStore.Products,
                key,
                CacheResources.Products,
                () => _posterService.GetProductsAsync(cancellationToken),
                existing,
                "revalidated",
                cancellationToken);
        }

        return last ?? BuildApiResponse(null);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var existing = _cacheStore.Categories.Get(PosterArrayCacheStore.DefaultKey);
        _logger.LogInformation("Revalidating categories cache...");
        return await RefreshWithLockAsync(
            _categoriesLock,
            _cacheStore.Categories,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.Categories,
            () => _posterService.GetCategoriesAsync(cancellationToken),
            existing,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidatePromotionsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        if (queryParams == null || queryParams.Count == 0)
        {
            _logger.LogInformation("Revalidating all promotions caches...");
            var keys = GetKnownKeys(_cacheStore.Promotions, BuildQueryKey(null));
            CachedResponse<JsonDocument?>? last = null;

            foreach (var key in keys)
            {
                var existing = _cacheStore.Promotions.Get(key);
                last = await RefreshWithLockAsync(
                    _promotionsLock,
                    _cacheStore.Promotions,
                    key,
                    CacheResources.Promotions,
                    () => _posterService.GetPromotionsAsync(null, cancellationToken),
                    existing,
                    "revalidated",
                    cancellationToken);
            }

            return last ?? BuildApiResponse(null);
        }

        var cacheKey = BuildQueryKey(queryParams);
        var cached = _cacheStore.Promotions.Get(cacheKey);
        _logger.LogInformation("Revalidating promotions cache ({CacheKey})...", cacheKey);
        return await RefreshWithLockAsync(
            _promotionsLock,
            _cacheStore.Promotions,
            cacheKey,
            CacheResources.Promotions,
            () => _posterService.GetPromotionsAsync(queryParams, cancellationToken),
            cached,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateClientsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Revalidating clients without server-side caching...");
        var data = await _posterService.GetClientsAsync(queryParams?.GetValueOrDefault("phone"), cancellationToken);
        var response = BuildApiResponse(data);
        await PublishRevalidationAsync(CacheResources.Clients, "revalidated", cancellationToken);
        return response;
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Revalidating client {ClientId} without server-side caching...", clientId);
        var data = await _posterService.GetClientAsync(clientId, cancellationToken);
        var response = BuildApiResponse(data);
        await PublishRevalidationAsync(CacheResources.Clients, "revalidated", cancellationToken);
        return response;
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateClientGroupsAsync(CancellationToken cancellationToken = default)
    {
        var existing = _cacheStore.ClientGroups.Get(PosterArrayCacheStore.DefaultKey);
        _logger.LogInformation("Revalidating client groups cache...");
        return await RefreshWithLockAsync(
            _clientGroupsLock,
            _cacheStore.ClientGroups,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.ClientGroups,
            () => _posterService.GetClientGroupsAsync(cancellationToken),
            existing,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateTransactionsAsync(
        Dictionary<string, string?> queryParams,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Revalidating transactions without server-side caching...");

        var courierId = queryParams.GetValueOrDefault("courier_id") ?? string.Empty;
        var dateFrom = queryParams.GetValueOrDefault("dateFrom") ?? string.Empty;
        var dateTo = queryParams.GetValueOrDefault("dateTo") ?? string.Empty;

        var data = await _posterService.GetTransactionsAsync(courierId, dateFrom, dateTo, cancellationToken);
        var response = BuildApiResponse(data);
        await PublishRevalidationAsync(CacheResources.Transactions, "revalidated", cancellationToken);
        return response;
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateSpotsAsync(CancellationToken cancellationToken = default)
    {
        var existing = _cacheStore.Spots.Get(PosterArrayCacheStore.DefaultKey);
        _logger.LogInformation("Revalidating spots cache...");
        return await RefreshWithLockAsync(
            _spotsLock,
            _cacheStore.Spots,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.Spots,
            () => _posterService.GetSpotsAsync(cancellationToken),
            existing,
            "revalidated",
            cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var existing = _cacheStore.Employees.Get(PosterArrayCacheStore.DefaultKey);
        _logger.LogInformation("Revalidating employees cache...");
        return await RefreshWithLockAsync(
            _employeesLock,
            _cacheStore.Employees,
            PosterArrayCacheStore.DefaultKey,
            CacheResources.Employees,
            () => _posterService.GetEmployeesAsync(cancellationToken),
            existing,
            "revalidated",
            cancellationToken);
    }

    public async Task<RevalidateAllResponse> RevalidateAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Revalidating all poster array caches...");

        await RevalidateAllProductsAsync(cancellationToken);
        await RevalidateCategoriesAsync(cancellationToken);
        await RevalidatePromotionsAsync(null, cancellationToken);
        await RevalidateClientGroupsAsync(cancellationToken);
        await RevalidateSpotsAsync(cancellationToken);
        await RevalidateEmployeesAsync(cancellationToken);

        return new RevalidateAllResponse
        {
            Message = "All Poster array caches refreshed",
            Timestamp = _timeProvider.GetUtcNow().UtcDateTime
        };
    }
}
