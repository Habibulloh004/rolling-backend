namespace Rolling.Domain.Chat;

public sealed class ChatMessage
{
    public Guid Id { get; }

    public Guid ThreadId { get; }

    public Guid SenderId { get; }

    public ChatParticipantRole SenderRole { get; }

    public ChatMessageContentType ContentType { get; }

    public string Body { get; }

    public DateTimeOffset SentAt { get; }

    public ChatMessageDeliveryStatus Status { get; private set; }

    private ChatMessage(
        Guid id,
        Guid threadId,
        Guid senderId,
        ChatParticipantRole senderRole,
        ChatMessageContentType contentType,
        string body,
        DateTimeOffset sentAt,
        ChatMessageDeliveryStatus status)
    {
        Id = id;
        ThreadId = threadId;
        SenderId = senderId;
        SenderRole = senderRole;
        ContentType = contentType;
        Body = body;
        SentAt = sentAt;
        Status = status;
    }

    public static ChatMessage Create(
        Guid threadId,
        Guid senderId,
        ChatParticipantRole senderRole,
        ChatMessageContentType contentType,
        string body,
        DateTimeOffset sentAt)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Message body is required.", nameof(body));
        }

        return new ChatMessage(
            Guid.NewGuid(),
            threadId,
            senderId,
            senderRole,
            contentType,
            body.Trim(),
            sentAt,
            ChatMessageDeliveryStatus.Sent);
    }

    public static ChatMessage Restore(
        Guid id,
        Guid threadId,
        Guid senderId,
        ChatParticipantRole senderRole,
        ChatMessageContentType contentType,
        string body,
        DateTimeOffset sentAt,
        ChatMessageDeliveryStatus status) =>
        new(id, threadId, senderId, senderRole, contentType, body, sentAt, status);

    public void MarkDelivered() => Status = ChatMessageDeliveryStatus.Delivered;

    public void MarkRead() => Status = ChatMessageDeliveryStatus.Read;
}
