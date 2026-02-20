using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

    public async Task<IReadOnlyList<(ChatThread Thread, ChatMessage? LastMessage, string? OrderNumber, int? OrderStatus, string? OrderCustomerName)>> GetAllWithLastMessageAsync(
        int take,
        int skip,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var length = Math.Clamp(take, 1, 100);

        // Get threads with their last message using a subquery
        var threads = await _dbContext.ChatThreads
            .Include(t => t.Participants)
            .AsNoTracking()
            .OrderByDescending(t => t.UpdatedAt)
            .Skip(skip)
            .Take(length)
            .ToListAsync(cancellationToken);

        if (threads.Count == 0)
        {
            return Array.Empty<(ChatThread Thread, ChatMessage? LastMessage, string? OrderNumber, int? OrderStatus, string? OrderCustomerName)>();
        }

        var threadIds = threads.Select(t => t.Id).ToArray();
        var orderIds = threads
            .Select(t => t.OrderId.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var lastMessages = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(m => threadIds.Contains(m.ThreadId))
            .GroupBy(m => m.ThreadId)
            .Select(g => g
                .OrderByDescending(m => m.SentAt)
                .ThenByDescending(m => m.Id)
                .First())
            .ToListAsync(cancellationToken);

        var orders = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new OrderThreadProjection(
                o.Id,
                o.OrderNumber,
                o.PosterIncomingOrderId,
                o.PosterTransactionId,
                (int)o.Status,
                o.FirstName,
                o.LastName))
            .ToListAsync(cancellationToken);

        var lastMessageByThreadId = lastMessages.ToDictionary(m => m.ThreadId, m => m, EqualityComparer<Guid>.Default);
        var orderByThreadOrderId = new Dictionary<Guid, OrderThreadProjection>();
        foreach (var order in orders)
        {
            if (TryResolveThreadOrderId(order.Id, out var threadOrderId) &&
                !orderByThreadOrderId.ContainsKey(threadOrderId))
            {
                orderByThreadOrderId[threadOrderId] = order;
            }
        }

        var unresolvedThreadOrderIds = threads
            .Select(t => t.OrderId)
            .Where(orderId => !orderByThreadOrderId.ContainsKey(orderId))
            .Distinct()
            .ToArray();

        if (unresolvedThreadOrderIds.Length > 0)
        {
            var minThreadCreatedAt = threads.Min(t => t.CreatedAt).UtcDateTime.AddDays(-30);
            var maxThreadCreatedAt = threads.Max(t => t.CreatedAt).UtcDateTime.AddDays(30);

            var candidateOrders = await _dbContext.Orders
                .AsNoTracking()
                .Where(o => o.CreatedAt >= minThreadCreatedAt && o.CreatedAt <= maxThreadCreatedAt)
                .Select(o => new OrderThreadProjection(
                    o.Id,
                    o.OrderNumber,
                    o.PosterIncomingOrderId,
                    o.PosterTransactionId,
                    (int)o.Status,
                    o.FirstName,
                    o.LastName))
                .ToListAsync(cancellationToken);

            var unresolvedSet = unresolvedThreadOrderIds.ToHashSet();
            foreach (var order in candidateOrders)
            {
                if (!TryResolveThreadOrderId(order.Id, out var threadOrderId) ||
                    !unresolvedSet.Contains(threadOrderId) ||
                    orderByThreadOrderId.ContainsKey(threadOrderId))
                {
                    continue;
                }

                orderByThreadOrderId[threadOrderId] = order;
            }
        }

        var result = new List<(ChatThread Thread, ChatMessage? LastMessage, string? OrderNumber, int? OrderStatus, string? OrderCustomerName)>(threads.Count);
        foreach (var thread in threads)
        {
            lastMessageByThreadId.TryGetValue(thread.Id, out var lastMessage);
            orderByThreadOrderId.TryGetValue(thread.OrderId, out var order);

            var displayOrderNumber = ResolveDisplayOrderNumber(
                order?.PosterIncomingOrderId,
                order?.PosterTransactionId,
                order?.OrderNumber);
            var orderCustomerName = BuildCustomerName(order?.FirstName, order?.LastName);

            result.Add((thread.ToDomain(), lastMessage?.ToDomain(), displayOrderNumber, order?.Status, orderCustomerName));
        }

        return result;
    }

    private static string? BuildCustomerName(string? firstName, string? lastName)
    {
        var first = firstName?.Trim();
        var last = lastName?.Trim();

        var fullName = string.Join(
            " ",
            new[] { first, last }.Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
    }

    private static string? ResolveDisplayOrderNumber(string? posterIncomingOrderId, string? posterTransactionId, string? orderNumber)
    {
        if (!string.IsNullOrWhiteSpace(posterIncomingOrderId))
        {
            return posterIncomingOrderId;
        }

        if (!string.IsNullOrWhiteSpace(posterTransactionId))
        {
            return posterTransactionId;
        }

        return orderNumber;
    }

    private static bool TryResolveThreadOrderId(string orderId, out Guid threadOrderId)
    {
        threadOrderId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return false;
        }

        if (Guid.TryParse(orderId.Trim(), out var parsed))
        {
            threadOrderId = parsed;
            return true;
        }

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(orderId.Trim()));
        threadOrderId = new Guid(hash, bigEndian: true);
        return true;
    }

    private sealed record OrderThreadProjection(
        string Id,
        string? OrderNumber,
        string? PosterIncomingOrderId,
        string? PosterTransactionId,
        int Status,
        string? FirstName,
        string? LastName);

    public async Task<IReadOnlyDictionary<Guid, int>> GetUnreadCountsAsync(
        IReadOnlyCollection<Guid> threadIds,
        ChatParticipantRole senderRole,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (threadIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var unread = await _dbContext.ChatMessages
            .AsNoTracking()
            .Where(message =>
                threadIds.Contains(message.ThreadId) &&
                message.SenderRole == senderRole &&
                message.Status != ChatMessageDeliveryStatus.Read)
            .GroupBy(message => message.ThreadId)
            .Select(group => new
            {
                ThreadId = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        return unread.ToDictionary(item => item.ThreadId, item => item.Count);
    }

    public async Task<(int TotalUnread, int ThreadsWithUnread)> GetUnreadSummaryAsync(
        ChatParticipantRole senderRole,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = _dbContext.ChatMessages
            .AsNoTracking()
            .Where(message =>
                message.SenderRole == senderRole &&
                message.Status != ChatMessageDeliveryStatus.Read);

        var totalUnread = await query.CountAsync(cancellationToken);
        if (totalUnread == 0)
        {
            return (0, 0);
        }

        var threadsWithUnread = await query
            .Select(message => message.ThreadId)
            .Distinct()
            .CountAsync(cancellationToken);

        return (totalUnread, threadsWithUnread);
    }

    public async Task MarkThreadMessagesAsReadAsync(
        Guid threadId,
        ChatParticipantRole senderRole,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _dbContext.ChatMessages
            .Where(message =>
                message.ThreadId == threadId &&
                message.SenderRole == senderRole &&
                message.Status != ChatMessageDeliveryStatus.Read)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.Status, ChatMessageDeliveryStatus.Read),
                cancellationToken);
    }
}
