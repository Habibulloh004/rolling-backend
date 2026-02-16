using Rolling.Domain.Chat;

namespace Rolling.Web.Models.Chat;

public sealed class MarkChatReadRequest
{
    public ChatParticipantRole ReaderRole { get; init; } = ChatParticipantRole.Support;
}
