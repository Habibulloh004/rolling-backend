using Rolling.Domain.Chat;

namespace Rolling.Web.Models.Chat;

public sealed class SendChatMessageRequest
{
    public Guid SenderId { get; init; }
    public ChatParticipantRole SenderRole { get; init; }
    public ChatMessageContentType ContentType { get; init; } = ChatMessageContentType.Text;
    public string Body { get; init; } = string.Empty;
}
