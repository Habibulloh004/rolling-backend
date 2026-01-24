using System.Text.Json;
using Rolling.Application.Chat.DTOs;
using Rolling.Domain.Chat;

namespace Rolling.Web.Realtime;

internal static class ChatSocketProtocol
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string SerializeBroadcast(ChatMessageDto message, string? clientMessageId)
    {
        var envelope = new ChatSocketBroadcast("message", message, clientMessageId);
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    public static string SerializePresence(Guid threadId, bool isOnline)
    {
        var envelope = new ChatSocketPresence("presence", new PresencePayload(threadId, isOnline));
        return JsonSerializer.Serialize(envelope, SerializerOptions);
    }

    public static ChatSocketEnvelope? DeserializeEnvelope(string payload)
    {
        return JsonSerializer.Deserialize<ChatSocketEnvelope>(payload, SerializerOptions);
    }

    internal sealed record ChatSocketEnvelope(string Type, ChatSocketInbound? Payload, string? ClientMessageId);

    internal sealed record ChatSocketInbound(ChatMessageContentType ContentType, string Body);

    private sealed record ChatSocketBroadcast(string Type, ChatMessageDto Payload, string? ClientMessageId);

    private sealed record ChatSocketPresence(string Type, PresencePayload Payload);

    private sealed record PresencePayload(Guid ThreadId, bool IsOnline);
}
