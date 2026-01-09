using System.Text.Json;
using Rolling.Application.Abstractions.Realtime;

namespace Rolling.Web.Realtime;

public sealed class WebSocketCacheRevalidationPublisher : ICacheRevalidationPublisher
{
    private readonly WebSocketConnectionManager _connections;
    private readonly ILogger<WebSocketCacheRevalidationPublisher> _logger;

    public WebSocketCacheRevalidationPublisher(
        WebSocketConnectionManager connections,
        ILogger<WebSocketCacheRevalidationPublisher> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task PublishAsync(CacheRevalidationEvent payload, CancellationToken cancellationToken = default)
    {
        var message = JsonSerializer.Serialize(new
        {
            type = "revalidate",
            resource = payload.Resource,
            changeType = payload.ChangeType,
            version = payload.Version,
            updatedAt = payload.UpdatedAt.ToString("O"),
            scope = payload.Scope
        });

        await _connections.BroadcastAsync(message, cancellationToken);
        _logger.LogInformation(
            "Broadcasted revalidation: {Resource} v{Version} ({ChangeType})",
            payload.Resource,
            payload.Version,
            payload.ChangeType ?? "update");
    }
}
