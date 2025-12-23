using System.Linq;
using Rolling.Domain.Chat;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class ChatThreadRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public ChatThreadStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ChatParticipantRecord> Participants { get; set; } = new List<ChatParticipantRecord>();

    public static ChatThreadRecord FromDomain(ChatThread thread)
    {
        var record = new ChatThreadRecord
        {
            Id = thread.Id,
            TenantId = thread.TenantId,
            OrderId = thread.OrderId,
            CustomerId = thread.CustomerId,
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt
        };

        record.Participants = thread.Participants
            .Select(participant => ChatParticipantRecord.FromDomain(participant))
            .ToList();

        return record;
    }

    public ChatThread ToDomain() =>
        ChatThread.Restore(
            Id,
            TenantId,
            OrderId,
            CustomerId,
            Status,
            CreatedAt,
            UpdatedAt,
            Participants.Select(participant => participant.ToDomain()));
}
