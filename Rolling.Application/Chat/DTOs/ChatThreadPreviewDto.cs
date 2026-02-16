namespace Rolling.Application.Chat.DTOs;

public sealed record ChatThreadPreviewDto(
    Guid Id,
    Guid? OrderId,
    string? OrderNumber,
    int? OrderStatus,
    Guid? CustomerId,
    string? CustomerName,
    string? LastMessage,
    DateTimeOffset? LastMessageAt,
    int UnreadCount,
    DateTimeOffset CreatedAt);
