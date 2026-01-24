using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Rolling.Domain.Chat;

namespace Rolling.Web.Realtime;

public sealed class ChatRealtimeCoordinator
{
    private sealed record ConnectionInfo(WebSocket Socket, ChatParticipantRole Role);

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, ConnectionInfo>> _groups = new();
    private readonly AdminChatCoordinator _adminCoordinator;

    public ChatRealtimeCoordinator(AdminChatCoordinator adminCoordinator)
    {
        _adminCoordinator = adminCoordinator;
    }

    /// <summary>
    /// Returns list of thread IDs that have at least one active customer WebSocket connection.
    /// </summary>
    public IReadOnlyList<Guid> GetActiveThreadIds()
    {
        return _groups
            .Where(kv => kv.Value.Values.Any(c => c.Socket.State == WebSocketState.Open && c.Role == ChatParticipantRole.Customer))
            .Select(kv => kv.Key)
            .ToList();
    }

    public async Task<string> AddAsync(Guid threadId, WebSocket socket, ChatParticipantRole role)
    {
        var connectionId = Guid.NewGuid().ToString("N");
        var group = _groups.GetOrAdd(threadId, _ => new ConcurrentDictionary<string, ConnectionInfo>());

        var hadCustomerBefore = group.Values.Any(c => c.Socket.State == WebSocketState.Open && c.Role == ChatParticipantRole.Customer);
        group[connectionId] = new ConnectionInfo(socket, role);

        // If a customer just connected and there wasn't one before, notify admins
        if (role == ChatParticipantRole.Customer && !hadCustomerBefore)
        {
            await BroadcastPresenceToAdminsAsync(threadId, isOnline: true);
            // Also broadcast to global admin connections
            await BroadcastPresenceToGlobalAdminsAsync(threadId, isOnline: true);
        }

        return connectionId;
    }

    public async Task BroadcastAsync(Guid threadId, string message, CancellationToken cancellationToken)
    {
        if (!_groups.TryGetValue(threadId, out var connections))
        {
            return;
        }

        var payload = Encoding.UTF8.GetBytes(message);
        var deadConnections = new List<string>();

        foreach (var (connectionId, info) in connections)
        {
            if (info.Socket.State != WebSocketState.Open)
            {
                deadConnections.Add(connectionId);
                continue;
            }

            try
            {
                await info.Socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
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
        if (!_groups.TryGetValue(threadId, out var connections))
        {
            return;
        }

        if (!connections.TryRemove(connectionId, out var info))
        {
            return;
        }

        try
        {
            if (info.Socket.State == WebSocketState.Open)
            {
                await info.Socket.CloseAsync(closeStatus, description, CancellationToken.None);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
        finally
        {
            info.Socket.Dispose();
        }

        // If a customer disconnected and no customers remain, notify admins
        if (info.Role == ChatParticipantRole.Customer)
        {
            var hasCustomerNow = connections.Values.Any(c => c.Socket.State == WebSocketState.Open && c.Role == ChatParticipantRole.Customer);
            if (!hasCustomerNow)
            {
                await BroadcastPresenceToAdminsAsync(threadId, isOnline: false);
                // Also broadcast to global admin connections
                await BroadcastPresenceToGlobalAdminsAsync(threadId, isOnline: false);
            }
        }

        if (connections.IsEmpty)
        {
            _groups.TryRemove(threadId, out _);
        }
    }

    private async Task BroadcastPresenceToAdminsAsync(Guid threadId, bool isOnline)
    {
        if (!_groups.TryGetValue(threadId, out var connections))
        {
            return;
        }

        var presenceMessage = ChatSocketProtocol.SerializePresence(threadId, isOnline);
        var payload = Encoding.UTF8.GetBytes(presenceMessage);

        foreach (var (_, info) in connections)
        {
            // Only send presence to admins/operators, not to customers
            if (info.Role == ChatParticipantRole.Customer)
            {
                continue;
            }

            if (info.Socket.State != WebSocketState.Open)
            {
                continue;
            }

            try
            {
                await info.Socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
                // Ignore send failures for presence updates
            }
        }
    }

    private async Task BroadcastPresenceToGlobalAdminsAsync(Guid threadId, bool isOnline)
    {
        if (!_adminCoordinator.HasConnections)
        {
            return;
        }

        var presenceMessage = ChatSocketProtocol.SerializePresence(threadId, isOnline);
        await _adminCoordinator.BroadcastAsync(presenceMessage);
    }

    public async Task BroadcastMessageToGlobalAdminsAsync(string serializedMessage)
    {
        if (!_adminCoordinator.HasConnections)
        {
            return;
        }

        await _adminCoordinator.BroadcastAsync(serializedMessage);
    }
}
