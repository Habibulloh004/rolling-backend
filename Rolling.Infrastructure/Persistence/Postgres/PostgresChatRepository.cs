using System.Linq;
using Microsoft.EntityFrameworkCore;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Domain.Chat;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Persistence.Postgres;

public sealed class PostgresChatRepository : IChatThreadRepository, IChatMessageRepository
{
    private readonly AppDbContext _dbContext;

    public PostgresChatRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ChatThread?> FindByOrderAsync(Guid tenantId, Guid orderId, Guid customerId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await _dbContext.ChatThreads
            .Include(thread => thread.Participants)
            .AsNoTracking()
            .FirstOrDefaultAsync(
                thread => thread.TenantId == tenantId &&
                          thread.OrderId == orderId &&
                          thread.CustomerId == customerId,
                cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<ChatThread?> FindByIdAsync(Guid threadId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await _dbContext.ChatThreads
            .Include(thread => thread.Participants)
            .AsNoTracking()
            .FirstOrDefaultAsync(thread => thread.Id == threadId, cancellationToken);

        return entity?.ToDomain();
    }

    public async Task<ChatThread> AddAsync(ChatThread thread, IEnumerable<ChatParticipant> participants, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = ChatThreadRecord.FromDomain(thread);
        await _dbContext.ChatThreads.AddAsync(entity, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return entity.ToDomain();
    }

    public async Task UpdateStatusAsync(Guid threadId, ChatThreadStatus status, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entity = await _dbContext.ChatThreads.FirstOrDefaultAsync(thread => thread.Id == threadId, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Status = status;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AppendAsync(ChatMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var record = ChatMessageRecord.FromDomain(message);
        await _dbContext.ChatMessages.AddAsync(record, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetRecentAsync(Guid threadId, int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = Math.Clamp(take, 1, 500);
        var entities = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId)
            .OrderByDescending(message => message.SentAt)
            .Take(length)
            .ToListAsync(cancellationToken);

        return entities.Select(entity => entity.ToDomain()).ToList();
    }

    public async Task<IReadOnlyList<ChatMessage>> GetBeforeAsync(Guid threadId, Guid beforeMessageId, int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pivot = await _dbContext.ChatMessages
            .AsNoTracking()
            .FirstOrDefaultAsync(message => message.Id == beforeMessageId && message.ThreadId == threadId, cancellationToken);

        if (pivot is null)
        {
            return Array.Empty<ChatMessage>();
        }

        var length = Math.Clamp(take, 1, 200);
        var entities = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(message => message.ThreadId == threadId && message.SentAt < pivot.SentAt)
            .OrderByDescending(message => message.SentAt)
            .Take(length)
            .ToListAsync(cancellationToken);

        return entities.Select(entity => entity.ToDomain()).ToList();
    }
}
