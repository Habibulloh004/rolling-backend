using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.DTOs;
using Rolling.Application.Chat.Queries;

namespace Rolling.Application.Chat.Contracts;

public interface IChatService
{
    Task<ChatThreadDto> OpenThreadAsync(OpenChatThreadCommand command, CancellationToken cancellationToken);

    Task<ChatMessageDto> SendAsync(SendChatMessageCommand command, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(GetChatMessagesQuery query, CancellationToken cancellationToken);
}
