using Rolling.Domain.Chat;

namespace Rolling.Application.Chat.Commands;

public sealed record SendChatMessageCommand(
    Guid ThreadId,
    Guid SenderId,
    ChatParticipantRole SenderRole,
    ChatMessageContentType ContentType,
    string Body);
