using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Rolling.Web.Realtime;

public sealed class PosterUpdatesSocketHandler
{
    private readonly WebSocketConnectionManager _connections;
    private readonly ILogger<PosterUpdatesSocketHandler> _logger;

    public PosterUpdatesSocketHandler(
        WebSocketConnectionManager connections,
        ILogger<PosterUpdatesSocketHandler> logger)
    {
        _connections = connections;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("WebSocket connection is required.");
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = _connections.AddSocket(socket);

        _logger.LogInformation("WebSocket connected for poster updates ({ConnectionId})", connectionId);

        await _connections.ReceiveLoopAsync(connectionId, socket, linkedCts.Token);

        _logger.LogInformation("WebSocket disconnected for poster updates ({ConnectionId})", connectionId);
    }
}
