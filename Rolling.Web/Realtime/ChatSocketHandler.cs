using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.Contracts;
using Rolling.Domain.Chat;

namespace Rolling.Web.Realtime;

public sealed class ChatSocketHandler
{
    private readonly ChatRealtimeCoordinator _coordinator;
    private readonly IChatService _chatService;
    private readonly ILogger<ChatSocketHandler> _logger;

    public ChatSocketHandler(
        ChatRealtimeCoordinator coordinator,
        IChatService chatService,
        ILogger<ChatSocketHandler> logger)
    {
        _coordinator = coordinator;
        _chatService = chatService;
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

        if (!TryResolveParameters(context, out var threadId, out var senderId, out var role))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("threadId, senderId, and role query parameters are required.");
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connectionId = _coordinator.Add(threadId, socket);

        _logger.LogInformation("WebSocket connected to chat thread {ThreadId} ({ConnectionId})", threadId, connectionId);

        try
        {
            await ReceiveLoopAsync(threadId, connectionId, socket, senderId, role, linkedCts.Token);
        }
        finally
        {
            await _coordinator.RemoveAsync(threadId, connectionId, socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, socket.CloseStatusDescription);
            _logger.LogInformation("WebSocket disconnected from chat thread {ThreadId} ({ConnectionId})", threadId, connectionId);
        }
    }

    private async Task ReceiveLoopAsync(
        Guid threadId,
        string connectionId,
        WebSocket socket,
        Guid senderId,
        ChatParticipantRole role,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];

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

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
            if (payload.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                await socket.SendAsync("pong"u8.ToArray(), WebSocketMessageType.Text, true, cancellationToken);
                continue;
            }

            var envelope = ChatSocketProtocol.DeserializeEnvelope(payload);
            if (envelope is null)
            {
                _logger.LogWarning("Failed to parse incoming chat payload: {Payload}", payload);
                continue;
            }

            if (envelope is null || !string.Equals(envelope.Type, "message", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var incoming = envelope.Payload;
            if (incoming is null || string.IsNullOrWhiteSpace(incoming.Body))
            {
                continue;
            }

            var command = new SendChatMessageCommand(
                threadId,
                senderId,
                role,
                incoming.ContentType,
                incoming.Body);

            var message = await _chatService.SendAsync(command, cancellationToken);
            var serialized = ChatSocketProtocol.SerializeBroadcast(message, envelope.ClientMessageId);
            await _coordinator.BroadcastAsync(threadId, serialized, cancellationToken);
        }
    }

    private static bool TryResolveParameters(HttpContext context, out Guid threadId, out Guid senderId, out ChatParticipantRole role)
    {
        threadId = Guid.Empty;
        senderId = Guid.Empty;
        role = ChatParticipantRole.Customer;

        var query = context.Request.Query;
        if (!Guid.TryParse(query["threadId"], out threadId))
        {
            return false;
        }

        if (!Guid.TryParse(query["senderId"], out senderId))
        {
            return false;
        }

        var roleRaw = query["role"];
        if (string.IsNullOrWhiteSpace(roleRaw) || !Enum.TryParse(roleRaw, true, out role))
        {
            return false;
        }

        return true;
    }
}
