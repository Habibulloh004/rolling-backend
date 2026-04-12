using Rolling.Application.Abstractions.Realtime;

namespace Rolling.Web.Realtime;

public sealed class WebSocketCacheRevalidationPublisher : ICacheRevalidationPublisher
{
    private readonly ILogger<WebSocketCacheRevalidationPublisher> _logger;

    public WebSocketCacheRevalidationPublisher(
        WebSocketConnectionManager connections,
        ILogger<WebSocketCacheRevalidationPublisher> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync(CacheRevalidationEvent payload, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Suppressed cache revalidation websocket broadcast: {Resource} v{Version} ({ChangeType})",
            payload.Resource,
            payload.Version,
            payload.ChangeType ?? "update");

        await Task.CompletedTask;
    }
}
