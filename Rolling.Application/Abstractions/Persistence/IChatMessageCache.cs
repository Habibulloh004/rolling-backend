using Rolling.Domain.Chat;

namespace Rolling.Application.Abstractions.Persistence;

public interface IChatMessageCache
{
    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid threadId, int take, CancellationToken cancellationToken);

    Task CacheAsync(ChatMessage message, int maxMessages, CancellationToken cancellationToken);

    Task WarmAsync(Guid threadId, IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken);
}
