using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Rolling.Web.Realtime;

public sealed class AdminChatCoordinator
{
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();

    public string Add(WebSocket socket)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        _connections[connectionId] = socket;
        return connectionId;
    }

    public void Remove(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var socket))
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None)
                        .GetAwaiter().GetResult();
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
        }
    }

    public async Task BroadcastAsync(string message, CancellationToken cancellationToken = default)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var deadConnections = new List<string>();

        foreach (var (connectionId, socket) in _connections)
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
            Remove(connectionId);
        }
    }

    public bool HasConnections => !_connections.IsEmpty;
}
