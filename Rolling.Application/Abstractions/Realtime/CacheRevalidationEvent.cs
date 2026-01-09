namespace Rolling.Application.Abstractions.Realtime;

public sealed record CacheRevalidationScope(
    string? OrgId = null,
    string? BranchId = null);

public sealed record CacheRevalidationEvent(
    string Resource,
    string? ChangeType,
    string Version,
    DateTimeOffset UpdatedAt,
    CacheRevalidationScope? Scope = null);

public interface ICacheRevalidationPublisher
{
    Task PublishAsync(CacheRevalidationEvent payload, CancellationToken cancellationToken = default);
}
