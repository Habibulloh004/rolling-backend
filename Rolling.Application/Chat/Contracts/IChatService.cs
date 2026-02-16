using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.DTOs;
using Rolling.Application.Chat.Queries;
using Rolling.Domain.Chat;

namespace Rolling.Application.Chat.Contracts;

public interface IChatService
{
    Task<ChatThreadDto> OpenThreadAsync(OpenChatThreadCommand command, CancellationToken cancellationToken);

    Task<ChatMessageDto> SendAsync(SendChatMessageCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(GetChatMessagesQuery query, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChatThreadPreviewDto>> GetThreadsAsync(int take, int skip, CancellationToken cancellationToken);

    Task MarkThreadReadAsync(Guid threadId, ChatParticipantRole readerRole, CancellationToken cancellationToken);

    Task<ChatUnreadSummaryDto> GetUnreadSummaryAsync(ChatParticipantRole readerRole, CancellationToken cancellationToken);
}
