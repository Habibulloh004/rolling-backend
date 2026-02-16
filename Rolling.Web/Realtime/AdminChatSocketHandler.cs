using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rolling.Web.Auth;

namespace Rolling.Web.Realtime;

public sealed class AdminChatSocketHandler
{
    private readonly AdminChatCoordinator _coordinator;
    private readonly AdminAuthService _authService;
    private readonly ILogger<AdminChatSocketHandler> _logger;

    public AdminChatSocketHandler(
        AdminChatCoordinator coordinator,
        AdminAuthService authService,
        ILogger<AdminChatSocketHandler> logger)
    {
        _coordinator = coordinator;
        _authService = authService;
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

        var token = ResolveAccessToken(context);
        if (string.IsNullOrWhiteSpace(token) || _authService.ValidateAccessToken(token) is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized.");
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = _coordinator.Add(socket);

        _logger.LogInformation("Admin WebSocket connected ({ConnectionId})", connectionId);

        try
        {
            await ReceiveLoopAsync(socket, linkedCts.Token);
        }
        finally
        {
            _coordinator.Remove(connectionId);
            _logger.LogInformation("Admin WebSocket disconnected ({ConnectionId})", connectionId);
        }
    }

    private static async Task ReceiveLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            // Handle ping/pong
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
                if (payload.Equals("ping", StringComparison.OrdinalIgnoreCase))
                {
                    await socket.SendAsync("pong"u8.ToArray(), WebSocketMessageType.Text, true, cancellationToken);
                }
            }
        }
    }

    private static string? ResolveAccessToken(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("token", out var tokenValues))
        {
            var value = tokenValues.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        if (context.Request.Query.TryGetValue("accessToken", out var accessTokenValues))
        {
            var value = accessTokenValues.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return authorization[7..].Trim();
    }
}
