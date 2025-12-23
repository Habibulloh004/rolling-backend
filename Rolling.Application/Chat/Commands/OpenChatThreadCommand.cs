namespace Rolling.Application.Chat.Commands;

public sealed record OpenChatThreadCommand(
    Guid TenantId,
    Guid OrderId,
    Guid CustomerId,
    Guid CustomerUserId,
    string CustomerDisplayName);
