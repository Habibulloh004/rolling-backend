namespace Rolling.Application.Chat.DTOs;

public sealed record ChatUnreadSummaryDto(
    int TotalUnread,
    int ThreadsWithUnread);
