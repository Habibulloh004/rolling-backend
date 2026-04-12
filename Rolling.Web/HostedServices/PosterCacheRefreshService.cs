using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Poster;

namespace Rolling.Web.HostedServices;

public sealed class PosterCacheRefreshService : BackgroundService
{
    private const int MinimumScheduledRefreshSeconds = 60;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PosterCacheRefreshOptions _options;
    private readonly ILogger<PosterCacheRefreshService> _logger;

    public PosterCacheRefreshService(
        IServiceScopeFactory scopeFactory,
        IOptions<PosterCacheRefreshOptions> options,
        ILogger<PosterCacheRefreshService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.WarmOnStartup)
        {
            await WarmCachesAsync(stoppingToken);
        }

        var tasks = new List<Task>
        {
            RunPeriodicAsync(
                "products",
                NormalizeIntervalSeconds("products", _options.ProductsSeconds),
                (cache, ct) => cache.RevalidateAllProductsAsync(ct, publishRevalidation: false),
                stoppingToken
            ),
            RunPeriodicAsync(
                "categories",
                NormalizeIntervalSeconds("categories", _options.CategoriesSeconds),
                (cache, ct) => cache.RevalidateCategoriesAsync(ct, publishRevalidation: false),
                stoppingToken
            ),
            RunPeriodicAsync(
                "promotions",
                NormalizeIntervalSeconds("promotions", _options.PromotionsSeconds),
                (cache, ct) => cache.RevalidatePromotionsAsync(null, ct, publishRevalidation: false),
                stoppingToken
            ),
            RunPeriodicAsync(
                "client-groups",
                NormalizeIntervalSeconds("client-groups", _options.ClientGroupsSeconds),
                (cache, ct) => cache.RevalidateClientGroupsAsync(ct, publishRevalidation: false),
                stoppingToken
            ),
            RunPeriodicAsync(
                "spots",
                NormalizeIntervalSeconds("spots", _options.SpotsSeconds),
                (cache, ct) => cache.RevalidateSpotsAsync(ct, publishRevalidation: false),
                stoppingToken
            ),
            RunPeriodicAsync(
                "employees",
                NormalizeIntervalSeconds("employees", _options.EmployeesSeconds),
                (cache, ct) => cache.RevalidateEmployeesAsync(ct, publishRevalidation: false),
                stoppingToken
            )
        };

        await Task.WhenAll(tasks);
    }

    private async Task WarmCachesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Warming poster caches on startup...");
            await WithPosterCacheAsync(cancellationToken, async cache =>
            {
                await cache.RevalidateAllProductsAsync(cancellationToken, publishRevalidation: false);
                await cache.RevalidateCategoriesAsync(cancellationToken, publishRevalidation: false);
                await cache.RevalidatePromotionsAsync(null, cancellationToken, publishRevalidation: false);
                await cache.RevalidateClientGroupsAsync(cancellationToken, publishRevalidation: false);
                await cache.RevalidateSpotsAsync(cancellationToken, publishRevalidation: false);
                await cache.RevalidateEmployeesAsync(cancellationToken, publishRevalidation: false);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warm poster caches");
        }
    }

    private int NormalizeIntervalSeconds(string name, int intervalSeconds)
    {
        if (intervalSeconds <= 0)
        {
            return intervalSeconds;
        }

        if (intervalSeconds < MinimumScheduledRefreshSeconds)
        {
            _logger.LogWarning(
                "Poster cache refresh interval for {CacheName} was set to {ConfiguredSeconds}s. Clamping to {MinimumSeconds}s to avoid cache churn.",
                name,
                intervalSeconds,
                MinimumScheduledRefreshSeconds);
            return MinimumScheduledRefreshSeconds;
        }

        return intervalSeconds;
    }

    private async Task RunPeriodicAsync(
        string name,
        int intervalSeconds,
        Func<ICachedPosterService, CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (intervalSeconds <= 0)
        {
            return;
        }

        var interval = TimeSpan.FromSeconds(intervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                _logger.LogInformation("Scheduled refresh: {CacheName}", name);
                await WithPosterCacheAsync(cancellationToken, cache => action(cache, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled refresh failed for {CacheName}", name);
            }
        }
    }

    private async Task WithPosterCacheAsync(CancellationToken cancellationToken, Func<ICachedPosterService, Task> action)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICachedPosterService>();
        cancellationToken.ThrowIfCancellationRequested();
        await action(cache);
    }
}
