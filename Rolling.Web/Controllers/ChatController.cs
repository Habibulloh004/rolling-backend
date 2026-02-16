using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.Contracts;
using Rolling.Application.Chat.Queries;
using Rolling.Domain.Chat;
using Rolling.Infrastructure.Notifications;
using Rolling.Infrastructure.Persistence.Postgres;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Web.Auth;
using Rolling.Web.Models.Chat;
using Rolling.Web.Realtime;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ChatRealtimeCoordinator _coordinator;
    private readonly IChatThreadRepository _threadRepository;
    private readonly AppDbContext _dbContext;
    private readonly NotificationService _pushService;
    private readonly NotificationTokenStore _tokenStore;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IChatService chatService,
        ChatRealtimeCoordinator coordinator,
        IChatThreadRepository threadRepository,
        AppDbContext dbContext,
        NotificationService pushService,
        NotificationTokenStore tokenStore,
        ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _coordinator = coordinator;
        _threadRepository = threadRepository;
        _dbContext = dbContext;
        _pushService = pushService;
        _tokenStore = tokenStore;
        _logger = logger;
    }

    [HttpGet("chat/threads")]
    [AdminAuthorize]
    public async Task<IActionResult> GetThreads(
        [FromQuery] int take = 20,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var threads = await _chatService.GetThreadsAsync(take, skip, cancellationToken);
        return Ok(threads);
    }

    [HttpGet("chat/active")]
    [AdminAuthorize]
    public IActionResult GetActiveThreads()
    {
        var activeThreadIds = _coordinator.GetActiveThreadIds();
        return Ok(activeThreadIds);
    }

    [HttpGet("chat/unread-summary")]
    [AdminAuthorize]
    public async Task<IActionResult> GetUnreadSummary(
        [FromQuery] ChatParticipantRole readerRole = ChatParticipantRole.Support,
        CancellationToken cancellationToken = default)
    {
        var summary = await _chatService.GetUnreadSummaryAsync(readerRole, cancellationToken);
        return Ok(summary);
    }

    [HttpPost("orders/{orderId:guid}/chat")]
    public async Task<IActionResult> OpenThread(
        Guid orderId,
        [FromBody] OpenChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        var command = new OpenChatThreadCommand(
            request.TenantId,
            orderId,
            request.CustomerId,
            request.CustomerUserId,
            request.CustomerDisplayName);

        var thread = await _chatService.OpenThreadAsync(command, cancellationToken);
        return Ok(thread);
    }

    [HttpGet("chat/{threadId:guid}/messages")]
    public async Task<IActionResult> GetMessages(
        Guid threadId,
        [FromQuery] GetMessagesRequest request,
        CancellationToken cancellationToken)
    {
        var query = new GetChatMessagesQuery(threadId, request.BeforeMessageId, request.Take);
        var messages = await _chatService.GetMessagesAsync(query, cancellationToken);
        return Ok(messages);
    }

    [HttpPost("chat/{threadId:guid}/messages")]
    public async Task<IActionResult> SendMessage(
        Guid threadId,
        [FromBody] SendChatMessageRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("Request body is required.");
        }

        if (request.SenderRole == ChatParticipantRole.Support &&
            await IsSupportWriteBlockedAsync(threadId, cancellationToken))
        {
            return Conflict(new
            {
                error = "Order is finished. Support can only read this chat."
            });
        }

        var command = new SendChatMessageCommand(
            threadId,
            request.SenderId,
            request.SenderRole,
            request.ContentType,
            request.Body);

        var message = await _chatService.SendAsync(command, cancellationToken);

        try
        {
            var serialized = ChatSocketProtocol.SerializeBroadcast(message, clientMessageId: null);
            await _coordinator.BroadcastAsync(threadId, serialized, cancellationToken);
            await _coordinator.BroadcastMessageToGlobalAdminsAsync(serialized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast chat message {MessageId} for thread {ThreadId}", message.Id, threadId);
        }

        if (request.SenderRole == ChatParticipantRole.Support)
        {
            await TrySendCustomerPushAsync(message, cancellationToken);
        }

        return Ok(message);
    }

    [HttpPost("chat/{threadId:guid}/read")]
    public async Task<IActionResult> MarkThreadRead(
        Guid threadId,
        [FromBody] MarkChatReadRequest? request,
        CancellationToken cancellationToken)
    {
        var readerRole = request?.ReaderRole ?? ChatParticipantRole.Support;
        await _chatService.MarkThreadReadAsync(threadId, readerRole, cancellationToken);
        return NoContent();
    }

    private async Task<bool> IsSupportWriteBlockedAsync(Guid threadId, CancellationToken cancellationToken)
    {
        var thread = await _threadRepository.FindByIdAsync(threadId, cancellationToken);
        if (thread is null)
        {
            return false;
        }

        var orderId = thread.OrderId.ToString();
        var orderStatus = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => (OrderStatus?)order.Status)
            .FirstOrDefaultAsync(cancellationToken);

        return orderStatus is OrderStatus.Delivered or OrderStatus.Cancelled;
    }

    private async Task TrySendCustomerPushAsync(
        Rolling.Application.Chat.DTOs.ChatMessageDto message,
        CancellationToken cancellationToken)
    {
        try
        {
            var thread = await _threadRepository.FindByIdAsync(message.ThreadId, cancellationToken);
            if (thread is null)
            {
                return;
            }

            var orderId = thread.OrderId.ToString();
            var order = await _dbContext.Orders
                .AsNoTracking()
                .Where(item => item.Id == orderId)
                .Select(item => new
                {
                    item.Id,
                    item.OrderNumber,
                    item.FcmToken
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (order is null || string.IsNullOrWhiteSpace(order.FcmToken))
            {
                return;
            }

            var language = ResolveLanguage(order.FcmToken);
            var contentPreview = message.ContentType == ChatMessageContentType.Image
                ? "Image"
                : Truncate(message.Body, 120);

            var title = order.OrderNumber.StartsWith("#", StringComparison.Ordinal)
                ? order.OrderNumber
                : $"#{order.OrderNumber}";
            var body = language switch
            {
                "ru" => $"Сообщение от администратора: {contentPreview}",
                "uz" => $"Administratordan xabar: {contentPreview}",
                _ => $"Message from support: {contentPreview}"
            };

            var data = new Dictionary<string, string>
            {
                ["type"] = "orderChatMessage",
                ["messageType"] = "chatMessage",
                ["orderId"] = order.Id,
                ["order_id"] = order.Id,
                ["orderNumber"] = order.OrderNumber,
                ["order_number"] = order.OrderNumber,
                ["threadId"] = message.ThreadId.ToString(),
                ["thread_id"] = message.ThreadId.ToString(),
                ["messageId"] = message.Id.ToString(),
                ["message_id"] = message.Id.ToString(),
                ["deeplink"] = $"/orders/{order.Id}/chat"
            };

            await _pushService.SendToDeviceAsync(
                order.FcmToken,
                language,
                "chatMessage",
                new NotificationPayload(title, body),
                data,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send chat push notification for thread {ThreadId}", message.ThreadId);
        }
    }

    private string ResolveLanguage(string token)
    {
        if (_tokenStore.TryGet(token, out var entry) &&
            !string.IsNullOrWhiteSpace(entry.Language) &&
            NotificationService.IsLanguageSupported(entry.Language))
        {
            return entry.Language!.Trim().ToLowerInvariant();
        }

        return "en";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return $"{normalized[..maxLength]}...";
    }
}
