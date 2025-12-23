using System.Linq;
using System.Text.Json;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Domain.Chat;
using Rolling.Infrastructure.Configuration;
using StackExchange.Redis;

namespace Rolling.Infrastructure.Persistence.Redis;

public sealed class RedisChatMessageCache : IChatMessageCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IConnectionMultiplexer _multiplexer;
    private readonly RedisOptions _options;

    public RedisChatMessageCache(IConnectionMultiplexer multiplexer, RedisOptions options)
    {
        _multiplexer = multiplexer;
        _options = options;
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid threadId, int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _multiplexer.GetDatabase();
        var length = Math.Clamp(take, 1, _options.ChatRecentMessageLimit);
        var key = BuildKey(threadId);
        var list = await db.ListRangeAsync(key, 0, length - 1);

        var result = new List<ChatMessage>(list.Length);
        foreach (var entry in list)
        {
            if (entry.IsNullOrEmpty)
            {
                continue;
            }

            var document = JsonSerializer.Deserialize<MessageDocument>(entry.ToString(), SerializerOptions);
            if (document is null)
            {
                continue;
            }

            result.Add(document.ToDomain());
        }

        return result;
    }

    public async Task CacheAsync(ChatMessage message, int maxMessages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var db = _multiplexer.GetDatabase();
        var document = MessageDocument.From(message);
        var payload = JsonSerializer.Serialize(document, SerializerOptions);
        var key = BuildKey(message.ThreadId);

        await db.ListLeftPushAsync(key, payload);
        var limit = Math.Max(1, Math.Min(maxMessages, _options.ChatRecentMessageLimit));
        await db.ListTrimAsync(key, 0, limit - 1);
    }

    public async Task WarmAsync(Guid threadId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (messages.Count == 0)
        {
            return;
        }

        var db = _multiplexer.GetDatabase();
        var key = BuildKey(threadId);
        await db.KeyDeleteAsync(key);

        var payloads = messages
            .OrderByDescending(message => message.SentAt)
            .Select(message => (RedisValue)JsonSerializer.Serialize(MessageDocument.From(message), SerializerOptions))
            .ToArray();

        if (payloads.Length > 0)
        {
            await db.ListLeftPushAsync(key, payloads);
            var limit = Math.Max(1, Math.Min(_options.ChatRecentMessageLimit, payloads.Length));
            await db.ListTrimAsync(key, 0, limit - 1);
        }
    }

    private string BuildKey(Guid threadId) => $"{_options.ChatThreadKeyPrefix}:{threadId:N}";

    private sealed record MessageDocument(
        Guid Id,
        Guid ThreadId,
        Guid SenderId,
        ChatParticipantRole SenderRole,
        ChatMessageContentType ContentType,
        string Body,
        DateTimeOffset SentAt,
        ChatMessageDeliveryStatus Status)
    {
        public static MessageDocument From(ChatMessage message) =>
            new(message.Id, message.ThreadId, message.SenderId, message.SenderRole, message.ContentType, message.Body, message.SentAt, message.Status);

        public ChatMessage ToDomain() => ChatMessage.Restore(Id, ThreadId, SenderId, SenderRole, ContentType, Body, SentAt, Status);
    }
}
