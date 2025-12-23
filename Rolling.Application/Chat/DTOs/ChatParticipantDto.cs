using Rolling.Domain.Chat;

namespace Rolling.Application.Chat.DTOs;

public sealed record ChatParticipantDto(Guid Id, Guid UserId, ChatParticipantRole Role, string DisplayName);
