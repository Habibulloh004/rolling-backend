using Rolling.Domain.Chat;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class ChatMessageRecord
{
    public Guid Id { get; set; }
    public Guid ThreadId { get; set; }
    public Guid SenderId { get; set; }
    public ChatParticipantRole SenderRole { get; set; }
    public ChatMessageContentType ContentType { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public ChatMessageDeliveryStatus Status { get; set; }

    public static ChatMessageRecord FromDomain(ChatMessage message) =>
        new()
        {
            Id = message.Id,
            ThreadId = message.ThreadId,
            SenderId = message.SenderId,
            SenderRole = message.SenderRole,
            ContentType = message.ContentType,
            Body = message.Body,
            SentAt = message.SentAt,
            Status = message.Status
        };

    public ChatMessage ToDomain() =>
        ChatMessage.Restore(Id, ThreadId, SenderId, SenderRole, ContentType, Body, SentAt, Status);
}
