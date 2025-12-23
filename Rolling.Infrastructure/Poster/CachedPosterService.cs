using System.Text.Json;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Memory;

namespace Rolling.Infrastructure.Poster;

public interface ICachedPosterService
{
    Task<CachedResponse<JsonDocument?>> GetProductsAsync(Dictionary<string, string?>? queryParams = null, CancellationToken cancellationToken = default);
    Task<CachedResponse<JsonDocument?>> GetCategoriesAsync(CancellationToken cancellationToken = default);
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
}

public class RevalidateAllResponse
{
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class CachedPosterService : ICachedPosterService
{
    private readonly PosterService _posterService;
    private readonly IInMemoryCacheService _cacheService;
    private readonly ILogger<CachedPosterService> _logger;

    public CachedPosterService(
        PosterService posterService,
        IInMemoryCacheService cacheService,
        ILogger<CachedPosterService> logger)
    {
        _posterService = posterService;
        _cacheService = cacheService;
        _logger = logger;
    }

    private async Task<CachedResponse<T>> FetchWithCacheAsync<T>(
        string cacheKey,
        Func<Task<T>> fetchFunction,
        TimeSpan? ttl = null)
    {
        var cached = _cacheService.Get<T>(cacheKey);
        if (cached != null)
        {
            return new CachedResponse<T>
            {
                Data = cached,
                Source = "cache",
                Cached = true
            };
        }

        _logger.LogInformation("📡 Fetching from Poster API: {CacheKey}", cacheKey);
        var data = await fetchFunction();

        _cacheService.Set(cacheKey, data, ttl);

        return new CachedResponse<T>
        {
            Data = data,
            Source = "api",
            Cached = false
        };
    }

    private string BuildQueryKey(Dictionary<string, string?>? queryParams)
    {
        if (queryParams == null || queryParams.Count == 0)
            return "{}";

        return JsonSerializer.Serialize(queryParams);
    }

    // ===== MENU ENDPOINTS =====

    public Task<CachedResponse<JsonDocument?>> GetProductsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:products:{BuildQueryKey(queryParams)}";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetProductsAsync(cancellationToken));
    }

    public Task<CachedResponse<JsonDocument?>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "poster:categories";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetCategoriesAsync(cancellationToken));
    }

    // ===== CLIENT ENDPOINTS =====

    public Task<CachedResponse<JsonDocument?>> GetClientsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:clients:{BuildQueryKey(queryParams)}";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetClientsAsync(queryParams?.GetValueOrDefault("phone"), cancellationToken),
            TimeSpan.FromMinutes(30));
    }

    public Task<CachedResponse<JsonDocument?>> GetClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:client:{clientId}";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetClientAsync(clientId, cancellationToken),
            TimeSpan.FromMinutes(30));
    }

    public Task<CachedResponse<JsonDocument?>> GetClientGroupsAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "poster:client-groups";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetClientGroupsAsync(cancellationToken));
    }

    public async Task<JsonDocument?> CreateClientAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var result = await _posterService.CreateClientAsync(payload, cancellationToken);

        // Invalidate client caches
        _cacheService.DeleteByPattern("poster:clients:*");
        _cacheService.DeleteByPattern("poster:client:*");

        return result;
    }

    // ===== ORDER ENDPOINTS =====

    public Task<JsonDocument?> CreateIncomingOrderAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        return _posterService.CreateIncomingOrderAsync(payload, cancellationToken);
    }

    // ===== TRANSACTION ENDPOINTS =====

    public Task<CachedResponse<JsonDocument?>> GetTransactionsAsync(
        Dictionary<string, string?> queryParams,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:transactions:{BuildQueryKey(queryParams)}";

        var courierId = queryParams.GetValueOrDefault("courier_id") ?? "";
        var dateFrom = queryParams.GetValueOrDefault("dateFrom") ?? "";
        var dateTo = queryParams.GetValueOrDefault("dateTo") ?? "";

        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetTransactionsAsync(courierId, dateFrom, dateTo, cancellationToken),
            TimeSpan.FromMinutes(5));
    }

    public Task<CachedResponse<JsonDocument?>> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:transaction:{transactionId}";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetTransactionAsync(transactionId, cancellationToken),
            TimeSpan.FromMinutes(10));
    }

    public Task<CachedResponse<JsonDocument?>> GetTransactionProductsAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:transaction-products:{transactionId}";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetTransactionProductsAsync(transactionId, cancellationToken),
            TimeSpan.FromMinutes(10));
    }

    // ===== LOCATION ENDPOINTS =====

    public Task<CachedResponse<JsonDocument?>> GetSpotsAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "poster:spots";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetSpotsAsync(cancellationToken),
            TimeSpan.FromHours(2));
    }

    // ===== EMPLOYEE ENDPOINTS =====

    public Task<CachedResponse<JsonDocument?>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = "poster:employees";
        return FetchWithCacheAsync(
            cacheKey,
            () => _posterService.GetEmployeesAsync(cancellationToken),
            TimeSpan.FromMinutes(30));
    }

    // ===== REVALIDATION METHODS =====

    public async Task<CachedResponse<JsonDocument?>> RevalidateProductsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:products:{BuildQueryKey(queryParams)}";
        _cacheService.Delete(cacheKey);
        _logger.LogInformation("🔄 Revalidating products...");
        return await GetProductsAsync(queryParams, cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateAllProductsAsync(CancellationToken cancellationToken = default)
    {
        _cacheService.DeleteByPattern("poster:products:*");
        _logger.LogInformation("🔄 Revalidating all products (cleared all product caches)...");
        return await GetProductsAsync(null, cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateCategoriesAsync(CancellationToken cancellationToken = default)
    {
        _cacheService.Delete("poster:categories");
        _logger.LogInformation("🔄 Revalidating categories...");
        return await GetCategoriesAsync(cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateClientsAsync(
        Dictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:clients:{BuildQueryKey(queryParams)}";
        _cacheService.Delete(cacheKey);
        _logger.LogInformation("🔄 Revalidating clients...");
        return await GetClientsAsync(queryParams, cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        _cacheService.Delete($"poster:client:{clientId}");
        _logger.LogInformation("🔄 Revalidating client {ClientId}...", clientId);
        return await GetClientAsync(clientId, cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateClientGroupsAsync(CancellationToken cancellationToken = default)
    {
        _cacheService.Delete("poster:client-groups");
        _logger.LogInformation("🔄 Revalidating client groups...");
        return await GetClientGroupsAsync(cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateTransactionsAsync(
        Dictionary<string, string?> queryParams,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"poster:transactions:{BuildQueryKey(queryParams)}";
        _cacheService.Delete(cacheKey);
        _logger.LogInformation("🔄 Revalidating transactions...");
        return await GetTransactionsAsync(queryParams, cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateSpotsAsync(CancellationToken cancellationToken = default)
    {
        _cacheService.Delete("poster:spots");
        _logger.LogInformation("🔄 Revalidating spots...");
        return await GetSpotsAsync(cancellationToken);
    }

    public async Task<CachedResponse<JsonDocument?>> RevalidateEmployeesAsync(CancellationToken cancellationToken = default)
    {
        _cacheService.Delete("poster:employees");
        _logger.LogInformation("🔄 Revalidating employees...");
        return await GetEmployeesAsync(cancellationToken);
    }

    public Task<RevalidateAllResponse> RevalidateAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔄 Revalidating ALL caches...");
        _cacheService.DeleteByPattern("poster:*");

        return Task.FromResult(new RevalidateAllResponse
        {
            Message = "All Poster caches cleared",
            Timestamp = DateTime.UtcNow
        });
    }
}
