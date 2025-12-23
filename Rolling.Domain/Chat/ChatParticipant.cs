namespace Rolling.Domain.Chat;

public sealed class ChatParticipant
{
    public Guid Id { get; }

    public Guid ThreadId { get; }

    public Guid UserId { get; }

    public ChatParticipantRole Role { get; }

    public string DisplayName { get; }

    private ChatParticipant(Guid id, Guid threadId, Guid userId, ChatParticipantRole role, string displayName)
    {
        Id = id;
        ThreadId = threadId;
        UserId = userId;
        Role = role;
        DisplayName = displayName;
    }

    public static ChatParticipant Create(Guid threadId, Guid userId, ChatParticipantRole role, string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        return new ChatParticipant(Guid.NewGuid(), threadId, userId, role, displayName.Trim());
    }

    public static ChatParticipant Restore(Guid id, Guid threadId, Guid userId, ChatParticipantRole role, string displayName) =>
        new(id, threadId, userId, role, displayName);
}
