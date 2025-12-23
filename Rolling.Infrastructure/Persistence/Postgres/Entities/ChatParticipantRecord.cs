using Rolling.Domain.Chat;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class ChatParticipantRecord
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public ChatParticipantRole Role { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public static ChatParticipantRecord FromDomain(ChatParticipant participant) =>
        new()
        {
            Id = participant.Id,
            ThreadId = participant.ThreadId,
            UserId = participant.UserId,
            Role = participant.Role,
            DisplayName = participant.DisplayName
        };

    public ChatParticipant ToDomain() => ChatParticipant.Restore(Id, ThreadId, UserId, Role, DisplayName);
}
