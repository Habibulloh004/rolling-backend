using Rolling.Domain.Chat;

namespace Rolling.Application.Chat.DTOs;

public sealed record ChatMessageDto(
    Guid Id,
    Guid ThreadId,
    Guid SenderId,
    ChatParticipantRole SenderRole,
    ChatMessageContentType ContentType,
    string Body,
    DateTimeOffset SentAt,
    ChatMessageDeliveryStatus Status);
