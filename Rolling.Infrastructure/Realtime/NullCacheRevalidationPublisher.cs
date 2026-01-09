using Rolling.Application.Abstractions.Realtime;

namespace Rolling.Infrastructure.Realtime;

public sealed class NullCacheRevalidationPublisher : ICacheRevalidationPublisher
{
    public Task PublishAsync(CacheRevalidationEvent payload, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
