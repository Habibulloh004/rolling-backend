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
                o.PaymentTransactionId,
                o.UserId,
                (int)o.Status,
                o.CreatedAt,
                o.Phone,
                o.FirstName,
                o.LastName))
            .ToListAsync(cancellationToken);

        var lastMessageByThreadId = lastMessages.ToDictionary(m => m.ThreadId, m => m, EqualityComparer<Guid>.Default);
        var orderByThreadOrderId = new Dictionary<Guid, OrderThreadProjection>();
        AddOrderCandidates(orders, orderByThreadOrderId, null);

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
                    o.PaymentTransactionId,
                    o.UserId,
                    (int)o.Status,
                    o.CreatedAt,
                    o.Phone,
                    o.FirstName,
                    o.LastName))
                .ToListAsync(cancellationToken);

            var unresolvedSet = unresolvedThreadOrderIds.ToHashSet();
            AddOrderCandidates(candidateOrders, orderByThreadOrderId, unresolvedSet);

            var unresolvedThreads = threads
                .Where(thread => unresolvedSet.Contains(thread.OrderId) && !orderByThreadOrderId.ContainsKey(thread.OrderId))
                .ToList();

            if (unresolvedThreads.Count > 0)
            {
                var allCandidateOrders = orders
                    .Concat(candidateOrders)
                    .GroupBy(order => order.Id, StringComparer.Ordinal)
                    .Select(group => group.First())
                    .ToList();

                var ordersByCustomerId = new Dictionary<Guid, List<OrderThreadProjection>>();
                foreach (var order in allCandidateOrders)
                {
                    if (!TryResolveThreadOrderId(order.UserId, out var customerId))
                    {
                        continue;
                    }

                    if (!ordersByCustomerId.TryGetValue(customerId, out var items))
                    {
                        items = new List<OrderThreadProjection>();
                        ordersByCustomerId[customerId] = items;
                    }

                    items.Add(order);
                }

                foreach (var thread in unresolvedThreads)
                {
                    if (!ordersByCustomerId.TryGetValue(thread.CustomerId, out var customerOrders) || customerOrders.Count == 0)
                    {
                        continue;
                    }

                    var ranked = customerOrders
                        .Select(order => new
                        {
                            Order = order,
                            DistanceMinutes = Math.Abs((order.CreatedAt - thread.CreatedAt.UtcDateTime).TotalMinutes)
                        })
                        .Where(item => item.DistanceMinutes <= 24 * 60)
                        .OrderBy(item => item.DistanceMinutes)
                        .Take(2)
                        .ToList();

                    if (ranked.Count == 0)
                    {
                        continue;
                    }

                    if (ranked.Count > 1 && ranked[0].DistanceMinutes + 30 >= ranked[1].DistanceMinutes)
                    {
                        continue;
                    }

                    orderByThreadOrderId[thread.OrderId] = ranked[0].Order;
                }

                var stillUnresolvedThreads = unresolvedThreads
                    .Where(thread => !orderByThreadOrderId.ContainsKey(thread.OrderId))
                    .OrderBy(thread => thread.CreatedAt)
                    .ToList();

                if (stillUnresolvedThreads.Count > 0)
                {
                    var usedOrderIds = orderByThreadOrderId.Values
                        .Select(order => order.Id)
                        .ToHashSet(StringComparer.Ordinal);

                    foreach (var thread in stillUnresolvedThreads)
                    {
                        var customerDisplayName = thread.Participants
                            .FirstOrDefault(participant => participant.Role == ChatParticipantRole.Customer)
                            ?.DisplayName;
                        var customerPhone = NormalizePhone(customerDisplayName);
                        if (string.IsNullOrWhiteSpace(customerPhone))
                        {
                            continue;
                        }

                        var phoneMatchedOrder = allCandidateOrders
                            .Where(order => !usedOrderIds.Contains(order.Id))
                            .Select(order => new
                            {
                                Order = order,
                                Phone = NormalizePhone(order.Phone),
                                DistanceMinutes = Math.Abs((order.CreatedAt - thread.CreatedAt.UtcDateTime).TotalMinutes)
                            })
                            .Where(item => !string.IsNullOrWhiteSpace(item.Phone) &&
                                           string.Equals(item.Phone, customerPhone, StringComparison.Ordinal) &&
                                           item.DistanceMinutes <= 24 * 60)
                            .OrderBy(item => item.DistanceMinutes)
                            .Select(item => item.Order)
                            .FirstOrDefault();

                        if (phoneMatchedOrder is null)
                        {
                            continue;
                        }

                        orderByThreadOrderId[thread.OrderId] = phoneMatchedOrder;
                        usedOrderIds.Add(phoneMatchedOrder.Id);
                    }

                    stillUnresolvedThreads = stillUnresolvedThreads
                        .Where(thread => !orderByThreadOrderId.ContainsKey(thread.OrderId))
                        .OrderBy(thread => thread.CreatedAt)
                        .ToList();

                    var unresolvedThreadsByName = stillUnresolvedThreads
                        .Select(thread => new
                        {
                            Thread = thread,
                            Name = NormalizePersonName(
                                thread.Participants
                                    .FirstOrDefault(participant => participant.Role == ChatParticipantRole.Customer)
                                    ?.DisplayName)
                        })
                        .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name!, StringComparer.Ordinal);

                    var availableOrdersByName = allCandidateOrders
                        .Where(order => !usedOrderIds.Contains(order.Id))
                        .Select(order => new
                        {
                            Order = order,
                            Name = NormalizePersonName(BuildCustomerName(order.FirstName, order.LastName))
                        })
                        .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                        .GroupBy(item => item.Name!, item => item.Order, StringComparer.Ordinal)
                        .ToDictionary(
                            group => group.Key,
                            group => group.OrderBy(order => order.CreatedAt).ToList(),
                            StringComparer.Ordinal);

                    foreach (var group in unresolvedThreadsByName)
                    {
                        if (!availableOrdersByName.TryGetValue(group.Key, out var candidates) || candidates.Count == 0)
                        {
                            continue;
                        }

                        var threadsForName = group
                            .Select(item => item.Thread)
                            .OrderBy(thread => thread.CreatedAt)
                            .ToList();

                        foreach (var thread in threadsForName)
                        {
                            if (candidates.Count == 0)
                            {
                                break;
                            }

                            var bestIndex = -1;
                            var bestDistance = double.MaxValue;
                            for (var i = 0; i < candidates.Count; i++)
                            {
                                var distance = Math.Abs((candidates[i].CreatedAt - thread.CreatedAt.UtcDateTime).TotalMinutes);
                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    bestIndex = i;
                                }
                            }

                            if (bestIndex < 0 || bestDistance > 24 * 60)
                            {
                                continue;
                            }

                            var chosen = candidates[bestIndex];
                            candidates.RemoveAt(bestIndex);
                            orderByThreadOrderId[thread.OrderId] = chosen;
                            usedOrderIds.Add(chosen.Id);
                        }
                    }

                    foreach (var thread in stillUnresolvedThreads)
                    {
                        var threadName = NormalizePersonName(
                            thread.Participants
                                .FirstOrDefault(participant => participant.Role == ChatParticipantRole.Customer)
                                ?.DisplayName);
                        if (string.IsNullOrWhiteSpace(threadName))
                        {
                            continue;
                        }

                        var nameMatched = allCandidateOrders
                            .Where(order => !usedOrderIds.Contains(order.Id))
                            .Select(order => new
                            {
                                Order = order,
                                NormalizedName = NormalizePersonName(BuildCustomerName(order.FirstName, order.LastName))
                            })
                            .Where(item => !string.IsNullOrWhiteSpace(item.NormalizedName) &&
                                           string.Equals(item.NormalizedName, threadName, StringComparison.Ordinal))
                            .Select(item => new
                            {
                                item.Order,
                                DistanceMinutes = Math.Abs((item.Order.CreatedAt - thread.CreatedAt.UtcDateTime).TotalMinutes)
                            })
                            .Where(item => item.DistanceMinutes <= 24 * 60)
                            .OrderBy(item => item.DistanceMinutes)
                            .Take(2)
                            .ToList();

                        if (nameMatched.Count == 0)
                        {
                            continue;
                        }

                        var matchedOrder = nameMatched[0].Order;
                        orderByThreadOrderId[thread.OrderId] = matchedOrder;
                        usedOrderIds.Add(matchedOrder.Id);
                    }
                }
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

    private static void AddOrderCandidates(
        IEnumerable<OrderThreadProjection> orders,
        IDictionary<Guid, OrderThreadProjection> orderByThreadOrderId,
        ISet<Guid>? filter)
    {
        foreach (var order in orders)
        {
            foreach (var candidateId in BuildThreadOrderCandidates(order))
            {
                if (filter is not null && !filter.Contains(candidateId))
                {
                    continue;
                }

                if (!orderByThreadOrderId.ContainsKey(candidateId))
                {
                    orderByThreadOrderId[candidateId] = order;
                }
            }
        }
    }

    private static IEnumerable<Guid> BuildThreadOrderCandidates(OrderThreadProjection order)
    {
        var identifiers = new[]
        {
            order.Id,
            NormalizeOrderIdentifier(order.OrderNumber),
            order.PosterIncomingOrderId,
            order.PosterTransactionId,
            order.PaymentTransactionId
        };

        var seen = new HashSet<Guid>();
        foreach (var identifier in identifiers)
        {
            if (!TryResolveThreadOrderId(identifier, out var candidateId) || !seen.Add(candidateId))
            {
                continue;
            }

            yield return candidateId;
        }
    }

    private static string? NormalizeOrderIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizePersonName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var collapsed = string.Join(
            " ",
            value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return collapsed.ToLowerInvariant();
    }

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length < 10)
        {
            return null;
        }

        return digits;
    }

    private static bool TryResolveThreadOrderId(string? orderId, out Guid threadOrderId)
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
        string? PaymentTransactionId,
        string? UserId,
        int Status,
        DateTime CreatedAt,
        string? Phone,
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
