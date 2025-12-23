using Rolling.Infrastructure.Notifications;

namespace Rolling.Web.HostedServices;

public sealed class NotificationCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxInactivity = TimeSpan.FromDays(30);

    private readonly NotificationTokenStore _tokenStore;
    private readonly ILogger<NotificationCleanupService> _logger;

    public NotificationCleanupService(NotificationTokenStore tokenStore, ILogger<NotificationCleanupService> logger)
    {
        _tokenStore = tokenStore;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _tokenStore.Cleanup(MaxInactivity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up notification tokens.");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }
}
