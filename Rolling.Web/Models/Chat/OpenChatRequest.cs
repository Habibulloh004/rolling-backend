namespace Rolling.Web.Models.Chat;

public sealed class OpenChatRequest
{
    public Guid TenantId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid CustomerUserId { get; init; }
    public string CustomerDisplayName { get; init; } = string.Empty;
}
