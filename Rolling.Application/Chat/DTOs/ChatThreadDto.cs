using Rolling.Domain.Chat;

namespace Rolling.Application.Chat.DTOs;

public sealed record ChatThreadDto(
    Guid Id,
    Guid TenantId,
    Guid OrderId,
    Guid CustomerId,
    ChatThreadStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyCollection<ChatParticipantDto> Participants,
    IReadOnlyCollection<ChatMessageDto> RecentMessages);
