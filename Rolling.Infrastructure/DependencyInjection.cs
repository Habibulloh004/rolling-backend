using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Application.Abstractions.Messaging;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Abstractions.Realtime;
using Rolling.Application.Abstractions.Time;
using Rolling.Infrastructure.Configuration;
using Rolling.Infrastructure.EventBus;
using Rolling.Infrastructure.Messaging;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Orders;
using Rolling.Infrastructure.Poster;
using Rolling.Infrastructure.Realtime;
using Rolling.Infrastructure.Payments;
using Rolling.Infrastructure.Persistence;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Redis;
using Rolling.Infrastructure.Time;
using StackExchange.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Rolling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<PostgresOptions>(configuration.GetSection(PostgresOptions.SectionName));
        services.Configure<FirebaseOptions>(configuration.GetSection(FirebaseOptions.SectionName));
        services.Configure<ClickOptions>(configuration.GetSection(ClickOptions.SectionName));
        services.Configure<PaymeOptions>(configuration.GetSection(PaymeOptions.SectionName));
        services.Configure<PlumOptions>(configuration.GetSection(PlumOptions.SectionName));
        services.Configure<PosterOptions>(configuration.GetSection(PosterOptions.SectionName));
        services.Configure<TelegramOptions>(configuration.GetSection(TelegramOptions.SectionName));
        services.Configure<PosterCacheRefreshOptions>(configuration.GetSection(PosterCacheRefreshOptions.SectionName));

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<RedisOptions>>().Value;
            var config = ConfigurationOptions.Parse(options.ConnectionString, true);
            config.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(config);
        });

        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var postgresOptions = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
            if (string.IsNullOrWhiteSpace(postgresOptions.ConnectionString))
            {
                throw new InvalidOperationException("Postgres connection string is not configured.");
            }

            options.UseNpgsql(postgresOptions.ConnectionString);
        });

        services.AddSingleton(provider =>
        {
            var messaging = CreateFirebaseMessaging(provider);
            return new FirebaseMessagingAccessor(messaging);
        });

        services.AddSingleton(provider => provider.GetRequiredService<IOptions<RedisOptions>>().Value);
        services.AddHttpClient<Rolling.Infrastructure.Messaging.TelegramService>();
        services.AddHttpClient<Rolling.Infrastructure.Poster.PosterService>();
        services.AddHttpClient<PlumPaymentService>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<PlumOptions>>().Value;
            var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
                ? "https://pay.myuzcard.uz/api"
                : options.BaseUrl;

            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");

            var timeout = options.RequestTimeoutSeconds <= 0 ? 100 : options.RequestTimeoutSeconds;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(timeout, 5, 300));

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });
        services.AddScoped<OrderProcessor>();
        services.AddSingleton<NotificationTokenStore>();
        services.AddSingleton<NotificationService>();
        services.AddScoped<ClickService>();
        services.AddScoped<PaymeService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IChatMessageCache, RedisChatMessageCache>();
        services.AddScoped<PostgresNotificationRepository>();
        services.AddScoped<PostgresChatRepository>();

        // Redis Notification Cache (like Banners - PostgreSQL is source of truth, Redis is cache)
        services.AddSingleton<IRedisNotificationCache, RedisNotificationCache>();
        services.AddScoped<INotificationRepository, PersistingNotificationRepository>();
        services.AddScoped<IChatThreadRepository>(provider => provider.GetRequiredService<PostgresChatRepository>());
        services.AddScoped<IChatMessageRepository>(provider => provider.GetRequiredService<PostgresChatRepository>());
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.TryAddSingleton<ICacheRevalidationPublisher, NullCacheRevalidationPublisher>();

        // Redis Banner Cache
        services.AddSingleton<Rolling.Infrastructure.Persistence.Redis.IRedisBannerCache, Rolling.Infrastructure.Persistence.Redis.RedisBannerCache>();

        // Redis Branch Config Cache
        services.AddSingleton<Rolling.Infrastructure.Persistence.Redis.IRedisBranchConfigCache, Rolling.Infrastructure.Persistence.Redis.RedisBranchConfigCache>();

        // Poster array caches (in-memory, infinite lifetime)
        services.AddSingleton<PosterArrayCacheStore>();

        // In-Memory Cache Service (for Poster API)
        services.AddSingleton<Rolling.Infrastructure.Persistence.Memory.IInMemoryCacheService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<Rolling.Infrastructure.Persistence.Memory.InMemoryCacheService>>();
            return new Rolling.Infrastructure.Persistence.Memory.InMemoryCacheService(logger);
        });

        // Cached Poster Service
        services.AddScoped<Rolling.Infrastructure.Poster.ICachedPosterService, Rolling.Infrastructure.Poster.CachedPosterService>();

        return services;
    }

    private static GoogleCredential ResolveFirebaseCredential(FirebaseOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.CredentialsPath))
        {
            return GoogleCredential.FromFile(options.CredentialsPath);
        }

        if (!string.IsNullOrWhiteSpace(options.CredentialJson))
        {
            return GoogleCredential.FromJson(options.CredentialJson);
        }

        return GoogleCredential.GetApplicationDefault();
    }

    private static FirebaseMessaging? CreateFirebaseMessaging(IServiceProvider provider)
    {
        var options = provider.GetRequiredService<IOptions<FirebaseOptions>>().Value;
        if (!options.EnableFirebaseMessaging)
        {
            return null;
        }

        var credential = ResolveFirebaseCredential(options);
        var firebaseOptions = new AppOptions
        {
            Credential = credential,
            ProjectId = options.ProjectId
        };

        FirebaseApp app;
        try
        {
            app = FirebaseApp.Create(firebaseOptions, $"rolling-{Guid.NewGuid():N}");
        }
        catch (ArgumentException)
        {
            app = FirebaseApp.DefaultInstance ?? FirebaseApp.Create(firebaseOptions);
        }

        return FirebaseAdmin.Messaging.FirebaseMessaging.GetMessaging(app);
    }
}
