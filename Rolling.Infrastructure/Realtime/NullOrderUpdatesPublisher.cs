using Rolling.Application.Abstractions.Realtime;

namespace Rolling.Infrastructure.Realtime;

public sealed class NullOrderUpdatesPublisher : IOrderUpdatesPublisher
{
    public Task PublishAsync(OrderUpdateEvent payload, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
