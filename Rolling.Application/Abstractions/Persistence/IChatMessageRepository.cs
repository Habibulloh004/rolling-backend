using Rolling.Domain.Chat;

namespace Rolling.Application.Abstractions.Persistence;

public interface IChatMessageRepository
{
    Task AppendAsync(ChatMessage message, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid threadId, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChatMessage>> GetBeforeAsync(Guid threadId, Guid beforeMessageId, int take, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> GetUnreadCountsAsync(
        IReadOnlyCollection<Guid> threadIds,
        ChatParticipantRole senderRole,
        CancellationToken cancellationToken);

    Task<(int TotalUnread, int ThreadsWithUnread)> GetUnreadSummaryAsync(
        ChatParticipantRole senderRole,
        CancellationToken cancellationToken);

    Task MarkThreadMessagesAsReadAsync(
        Guid threadId,
        ChatParticipantRole senderRole,
        CancellationToken cancellationToken);
}
