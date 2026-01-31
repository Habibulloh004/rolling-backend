using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

namespace Rolling.Web.Realtime;

public sealed class OrderUpdatesConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocket> _sockets = new();

    public string AddSocket(WebSocket socket)
    {
        var connectionId = Guid.NewGuid().ToString();
        _sockets[connectionId] = socket;
        return connectionId;
    }

    public async Task ReceiveLoopAsync(string connectionId, WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                {
                    var incoming = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    if (incoming.Equals("ping", StringComparison.OrdinalIgnoreCase))
                    {
                        await socket.SendAsync("pong"u8.ToArray(), WebSocketMessageType.Text, true, cancellationToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Connection aborted by the client/request pipeline.
        }
        catch (WebSocketException)
        {
            // Swallow transport errors and close the socket below.
        }
        finally
        {
            await RemoveSocketAsync(connectionId, socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure, socket.CloseStatusDescription);
        }
    }

    public async Task BroadcastAsync(string message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(payload);
        var deadSockets = new List<string>();

        foreach (var (connectionId, socket) in _sockets)
        {
            if (socket.State != WebSocketState.Open)
            {
                deadSockets.Add(connectionId);
                continue;
            }

            try
            {
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, cancellationToken);
            }
            catch
            {
                deadSockets.Add(connectionId);
            }
        }

        foreach (var connectionId in deadSockets)
        {
            await RemoveSocketAsync(connectionId, WebSocketCloseStatus.InternalServerError, "Socket no longer available");
        }
    }

    private async Task RemoveSocketAsync(string connectionId, WebSocketCloseStatus closeStatus, string? description)
    {
        if (!_sockets.TryRemove(connectionId, out var socket))
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
            // Ignore cleanup errors.
        }
        finally
        {
            socket.Dispose();
        }
    }
}
