using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Redis;
using Rolling.Application.Abstractions.Persistence;
using StackExchange.Redis;

namespace Rolling.Web.HostedServices;

public sealed class RedisWarmupService : BackgroundService
{
    private static readonly string[] BannerLanguages = { "en", "ru", "uz" };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RedisOptions _redisOptions;
    private readonly ILogger<RedisWarmupService> _logger;

    public RedisWarmupService(
        IServiceScopeFactory scopeFactory,
        IOptions<RedisOptions> redisOptions,
        ILogger<RedisWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _redisOptions = redisOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await WarmupAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ignore shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis warmup failed");
        }
    }

    private async Task WarmupAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Redis warmup started");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notificationCache = scope.ServiceProvider.GetRequiredService<IRedisNotificationCache>();
        var bannerCache = scope.ServiceProvider.GetRequiredService<IRedisBannerCache>();
        var branchCache = scope.ServiceProvider.GetRequiredService<IRedisBranchConfigCache>();
        var chatCache = scope.ServiceProvider.GetRequiredService<IChatMessageCache>();
        var redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();

        await WarmNotificationsAsync(dbContext, notificationCache, cancellationToken);
        await WarmBannersAsync(dbContext, bannerCache, cancellationToken);
        await WarmBranchConfigsAsync(dbContext, branchCache, cancellationToken);
        await WarmChatAsync(dbContext, chatCache, redis, cancellationToken);

        _logger.LogInformation("Redis warmup completed");
    }

    private async Task WarmNotificationsAsync(
        AppDbContext dbContext,
        IRedisNotificationCache cache,
        CancellationToken cancellationToken)
    {
        await cache.InvalidateNotificationCacheAsync(cancellationToken);

        var take = Math.Clamp(_redisOptions.HistorySize, 1, 1000);
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (notifications.Count > 0)
        {
            await cache.SetNotificationsAsync(notifications, cancellationToken);
            _logger.LogInformation("Redis warmup: cached {Count} notifications", notifications.Count);
        }
    }

    private async Task WarmBannersAsync(
        AppDbContext dbContext,
        IRedisBannerCache cache,
        CancellationToken cancellationToken)
    {
        await cache.InvalidateBannerCacheAsync(cancellationToken);

        var banners = await dbContext.Banners
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);

        if (banners.Count == 0)
        {
            return;
        }

        await cache.SetBannersAsync(banners, null, cancellationToken);

        foreach (var lang in BannerLanguages)
        {
            var langBanners = banners.Where(b => string.Equals(b.Lang, lang, StringComparison.OrdinalIgnoreCase)).ToList();
            if (langBanners.Count > 0)
            {
                await cache.SetBannersAsync(langBanners, lang, cancellationToken);
            }
        }

        foreach (var banner in banners)
        {
            await cache.SetBannerAsync(banner, cancellationToken);
        }

        _logger.LogInformation("Redis warmup: cached {Count} banners", banners.Count);
    }

    private async Task WarmBranchConfigsAsync(
        AppDbContext dbContext,
        IRedisBranchConfigCache cache,
        CancellationToken cancellationToken)
    {
        await cache.InvalidateBranchConfigCacheAsync(cancellationToken);

        var configs = await dbContext.BranchConfigurations
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (configs.Count == 0)
        {
            return;
        }

        await cache.SetBranchConfigurationsAsync(configs, cancellationToken);
        _logger.LogInformation("Redis warmup: cached {Count} branch configurations", configs.Count);
    }

    private async Task WarmChatAsync(
        AppDbContext dbContext,
        IChatMessageCache cache,
        IConnectionMultiplexer redis,
        CancellationToken cancellationToken)
    {
        await ClearChatKeysAsync(redis, cancellationToken);

        var threadIds = await dbContext.ChatThreads
            .AsNoTracking()
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);

        if (threadIds.Count == 0)
        {
            return;
        }

        var take = Math.Clamp(_redisOptions.ChatRecentMessageLimit, 1, 500);

        foreach (var threadId in threadIds)
        {
            var messages = await dbContext.ChatMessages
                .AsNoTracking()
                .Where(m => m.ThreadId == threadId)
                .OrderByDescending(m => m.SentAt)
                .Take(take)
                .ToListAsync(cancellationToken);

            if (messages.Count == 0)
            {
                continue;
            }

            var domainMessages = messages
                .Select(m => m.ToDomain())
                .ToList();

            await cache.WarmAsync(threadId, domainMessages, cancellationToken);
        }

        _logger.LogInformation("Redis warmup: cached chat messages for {Count} threads", threadIds.Count);
    }

    private async Task ClearChatKeysAsync(IConnectionMultiplexer redis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var endpoints = redis.GetEndPoints();
        if (endpoints.Length == 0)
        {
            return;
        }

        var server = redis.GetServer(endpoints.First());
        var pattern = $"{_redisOptions.ChatThreadKeyPrefix}:*";
        var keys = server.Keys(pattern: pattern).ToArray();

        if (keys.Length == 0)
        {
            return;
        }

        var db = redis.GetDatabase();
        var batch = db.CreateBatch();
        var tasks = new List<Task>(keys.Length);

        foreach (var key in keys)
        {
            tasks.Add(batch.KeyDeleteAsync(key));
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }
}
