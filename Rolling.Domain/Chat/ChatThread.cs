namespace Rolling.Domain.Chat;

public sealed class ChatThread
{
    private readonly List<ChatParticipant> _participants = new();

    public Guid Id { get; }

    public Guid TenantId { get; }

    public Guid OrderId { get; }

    public Guid CustomerId { get; }

    public ChatThreadStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ChatParticipant> Participants => _participants;

    private ChatThread(
        Guid id,
        Guid tenantId,
        Guid orderId,
        Guid customerId,
        ChatThreadStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<ChatParticipant>? participants = null)
    {
        Id = id;
        TenantId = tenantId;
        OrderId = orderId;
        CustomerId = customerId;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        if (participants is not null)
        {
            _participants.AddRange(participants);
        }
    }

    public static ChatThread Create(Guid tenantId, Guid orderId, Guid customerId, DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(tenantId));
        }

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("Order id is required.", nameof(orderId));
        }

        if (customerId == Guid.Empty)
        {
            throw new ArgumentException("Customer id is required.", nameof(customerId));
        }

        return new ChatThread(
            Guid.NewGuid(),
            tenantId,
            orderId,
            customerId,
            ChatThreadStatus.Open,
            createdAt,
            createdAt);
    }

    public static ChatThread Restore(
        Guid id,
        Guid tenantId,
        Guid orderId,
        Guid customerId,
        ChatThreadStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IEnumerable<ChatParticipant>? participants = null) =>
        new(id, tenantId, orderId, customerId, status, createdAt, updatedAt, participants);

    public void AddParticipant(ChatParticipant participant, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(participant);
        _participants.Add(participant);
        UpdatedAt = timestamp;
    }

    public void UpdateStatus(ChatThreadStatus status, DateTimeOffset timestamp)
    {
        Status = status;
        UpdatedAt = timestamp;
    }
}
