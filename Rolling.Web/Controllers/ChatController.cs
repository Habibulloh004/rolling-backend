using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.Contracts;
using Rolling.Application.Chat.Queries;
using Rolling.Web.Models.Chat;
using Rolling.Web.Realtime;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ChatRealtimeCoordinator _coordinator;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatService chatService, ChatRealtimeCoordinator coordinator, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _coordinator = coordinator;
        _logger = logger;
    }

    [HttpGet("chat/threads")]
    public async Task<IActionResult> GetThreads(
        [FromQuery] int take = 20,
        [FromQuery] int skip = 0,
        CancellationToken cancellationToken = default)
    {
        var threads = await _chatService.GetThreadsAsync(take, skip, cancellationToken);
        return Ok(threads);
    }

    [HttpGet("chat/active")]
    public IActionResult GetActiveThreads()
    {
        var activeThreadIds = _coordinator.GetActiveThreadIds();
        return Ok(activeThreadIds);
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
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to broadcast chat message {MessageId} for thread {ThreadId}", message.Id, threadId);
        }

        return Ok(message);
    }
}
