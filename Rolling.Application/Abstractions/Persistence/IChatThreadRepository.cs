using Rolling.Domain.Chat;

namespace Rolling.Application.Abstractions.Persistence;

public interface IChatThreadRepository
{
    Task<ChatThread?> FindByOrderAsync(Guid tenantId, Guid orderId, Guid customerId, CancellationToken cancellationToken);

    Task<ChatThread?> FindByIdAsync(Guid threadId, CancellationToken cancellationToken);

    Task<ChatThread> AddAsync(ChatThread thread, IEnumerable<ChatParticipant> participants, CancellationToken cancellationToken);

    Task UpdateStatusAsync(Guid threadId, ChatThreadStatus status, CancellationToken cancellationToken);
}
