namespace Rolling.Web.Services.Webhooks;

public interface IWebhookMessageStore
{
    void Add(WebhookMessage message);

    IReadOnlyCollection<WebhookMessage> GetAll();
}

public sealed class InMemoryWebhookMessageStore : IWebhookMessageStore
{
    private readonly List<WebhookMessage> _messages = new();
    private readonly object _syncRoot = new();

    public void Add(WebhookMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        lock (_syncRoot)
        {
            _messages.Add(message);
        }
    }

    public IReadOnlyCollection<WebhookMessage> GetAll()
    {
        lock (_syncRoot)
        {
            return _messages.ToArray();
        }
    }
}

public sealed record WebhookMessage(Guid Id, DateTimeOffset ReceivedAt, string Payload);
