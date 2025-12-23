namespace Rolling.Application.Chat.Queries;

public sealed record GetChatMessagesQuery(Guid ThreadId, Guid? BeforeMessageId, int Take = 50);
