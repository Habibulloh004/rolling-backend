using Microsoft.Extensions.Logging;
using Rolling.Application.Abstractions.Persistence;
using Rolling.Application.Abstractions.Time;
using Rolling.Application.Chat.Commands;
using Rolling.Application.Chat.Contracts;
using Rolling.Application.Chat.DTOs;
using Rolling.Application.Chat.Queries;
using Rolling.Domain.Chat;

namespace Rolling.Application.Chat.Services;

public sealed class ChatService : IChatService
{
    private readonly IChatThreadRepository _threadRepository;
    private readonly IChatMessageRepository _messageRepository;
    private readonly IChatMessageCache _messageCache;
    private readonly IClock _clock;
    private readonly ILogger<ChatService> _logger;
    private const int RecentMessageLimit = 100;

    public ChatService(
        IChatThreadRepository threadRepository,
        IChatMessageRepository messageRepository,
        IChatMessageCache messageCache,
        IClock clock,
        ILogger<ChatService> logger)
    {
        _threadRepository = threadRepository;
        _messageRepository = messageRepository;
        _messageCache = messageCache;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ChatThreadDto> OpenThreadAsync(OpenChatThreadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var thread = await _threadRepository.FindByOrderAsync(
            command.TenantId,
            command.OrderId,
            command.CustomerId,
            cancellationToken);

        if (thread is null)
        {
            var createdAt = _clock.UtcNow;
            thread = ChatThread.Create(command.TenantId, command.OrderId, command.CustomerId, createdAt);
            var participant = ChatParticipant.Create(thread.Id, command.CustomerUserId, ChatParticipantRole.Customer, command.CustomerDisplayName);
            thread.AddParticipant(participant, createdAt);
            thread = await _threadRepository.AddAsync(thread, thread.Participants, cancellationToken);
        }

        var recentMessages = await LoadRecentMessagesAsync(thread.Id, cancellationToken);
        return MapThread(thread, recentMessages);
    }

    public async Task<ChatMessageDto> SendAsync(SendChatMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var thread = await _threadRepository.FindByIdAsync(command.ThreadId, cancellationToken)
            ?? throw new InvalidOperationException($"Chat thread {command.ThreadId} was not found.");

        var message = ChatMessage.Create(
            thread.Id,
            command.SenderId,
            command.SenderRole,
            command.ContentType,
            command.Body,
            _clock.UtcNow);

        await _messageRepository.AppendAsync(message, cancellationToken);
        await _messageCache.CacheAsync(message, RecentMessageLimit, cancellationToken);

        _logger.LogInformation("Chat message {MessageId} stored for thread {ThreadId}", message.Id, message.ThreadId);
        return MapMessage(message);
    }

    public async Task<IReadOnlyCollection<ChatMessageDto>> GetMessagesAsync(GetChatMessagesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ChatMessage> messages;
        if (query.BeforeMessageId is null)
        {
            messages = await LoadRecentMessagesAsync(query.ThreadId, cancellationToken);
        }
        else
        {
            messages = await _messageRepository.GetBeforeAsync(query.ThreadId, query.BeforeMessageId.Value, query.Take, cancellationToken);
        }

        return messages.Select(MapMessage).ToList();
    }

    private async Task<IReadOnlyList<ChatMessage>> LoadRecentMessagesAsync(Guid threadId, CancellationToken cancellationToken)
    {
        var cached = await _messageCache.GetRecentAsync(threadId, RecentMessageLimit, cancellationToken);
        if (cached.Count > 0)
        {
            return cached;
        }

        var fromStorage = await _messageRepository.GetRecentAsync(threadId, RecentMessageLimit, cancellationToken);
        if (fromStorage.Count > 0)
        {
            await _messageCache.WarmAsync(threadId, fromStorage, cancellationToken);
        }

        return fromStorage;
    }

    private static ChatThreadDto MapThread(ChatThread thread, IReadOnlyCollection<ChatMessage> messages)
    {
        var participants = thread.Participants.Select(MapParticipant).ToList();
        var messageDtos = messages.Select(MapMessage).ToList();
        return new ChatThreadDto(
            thread.Id,
            thread.TenantId,
            thread.OrderId,
            thread.CustomerId,
            thread.Status,
            thread.CreatedAt,
            thread.UpdatedAt,
            participants,
            messageDtos);
    }

    private static ChatParticipantDto MapParticipant(ChatParticipant participant) =>
        new(participant.Id, participant.UserId, participant.Role, participant.DisplayName);

    private static ChatMessageDto MapMessage(ChatMessage message) =>
        new(
            message.Id,
            message.ThreadId,
            message.SenderId,
            message.SenderRole,
            message.ContentType,
            message.Body,
            message.SentAt,
            message.Status);

    public async Task<IReadOnlyCollection<ChatThreadPreviewDto>> GetThreadsAsync(int take, int skip, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var threadsWithMessages = await _threadRepository.GetAllWithLastMessageAsync(take, skip, cancellationToken);

        return threadsWithMessages.Select(item =>
        {
            var thread = item.Thread;
            var lastMessage = item.LastMessage;
            var orderNumber = item.OrderNumber;
            var customerName = thread.Participants
                .FirstOrDefault(p => p.Role == ChatParticipantRole.Customer)?.DisplayName;

            return new ChatThreadPreviewDto(
                thread.Id,
                thread.OrderId,
                orderNumber,
                thread.CustomerId,
                customerName,
                lastMessage?.Body,
                lastMessage?.SentAt,
                0, // UnreadCount - can be implemented later
                thread.CreatedAt);
        }).ToList();
    }
}
