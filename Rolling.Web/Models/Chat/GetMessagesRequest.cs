namespace Rolling.Web.Models.Chat;

public sealed class GetMessagesRequest
{
    public Guid? BeforeMessageId { get; init; }
    public int Take { get; init; } = 50;
}
