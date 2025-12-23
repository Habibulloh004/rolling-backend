using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Rolling.Web.Realtime;

public sealed class ChatRealtimeCoordinator
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, WebSocket>> _groups = new();

    public string Add(Guid threadId, WebSocket socket)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var group = _groups.GetOrAdd(threadId, _ => new ConcurrentDictionary<string, WebSocket>());
        group[connectionId] = socket;
        return connectionId;
    }

    public async Task BroadcastAsync(Guid threadId, string message, CancellationToken cancellationToken)
    {
        if (!_groups.TryGetValue(threadId, out var sockets))
        {
            return;
        }

        var payload = Encoding.UTF8.GetBytes(message);
        var deadConnections = new List<string>();

        foreach (var (connectionId, socket) in sockets)
        {
            if (socket.State != WebSocketState.Open)
            {
                deadConnections.Add(connectionId);
                continue;
            }

            try
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch
            {
                deadConnections.Add(connectionId);
            }
        }

        foreach (var connectionId in deadConnections)
        {
            await RemoveAsync(threadId, connectionId, WebSocketCloseStatus.InternalServerError, "Connection lost");
        }
    }

    public async Task RemoveAsync(Guid threadId, string connectionId, WebSocketCloseStatus closeStatus, string? description)
    {
        if (!_groups.TryGetValue(threadId, out var sockets))
        {
            return;
        }

        if (!sockets.TryRemove(connectionId, out var socket))
        {
            return;
        }

        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(closeStatus, description, CancellationToken.None);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
        finally
        {
            socket.Dispose();
        }

        if (sockets.IsEmpty)
        {
            _groups.TryRemove(threadId, out _);
        }
    }
}
