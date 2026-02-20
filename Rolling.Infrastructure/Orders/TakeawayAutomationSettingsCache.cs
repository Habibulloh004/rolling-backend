namespace Rolling.Infrastructure.Orders;

public sealed record TakeawayAutomationSettingsSnapshot(
    int PreparingDelaySeconds,
    int ReadyForPickupDelaySeconds,
    DateTime UpdatedAt,
    DateTime LoadedAt,
    bool IsInitialized);

/// <summary>
/// Caches takeaway automation settings in memory and refreshes only when needed.
/// </summary>
public sealed class TakeawayAutomationSettingsCache
{
    private readonly object _sync = new();
    private TakeawayAutomationSettingsSnapshot _snapshot = new(
        PreparingDelaySeconds: 300,
        ReadyForPickupDelaySeconds: 1800,
        UpdatedAt: DateTime.MinValue,
        LoadedAt: DateTime.MinValue,
        IsInitialized: false);

    public TakeawayAutomationSettingsSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    public void Set(int preparingDelaySeconds, int readyForPickupDelaySeconds, DateTime updatedAt, DateTime loadedAt)
    {
        lock (_sync)
        {
            _snapshot = new TakeawayAutomationSettingsSnapshot(
                preparingDelaySeconds,
                readyForPickupDelaySeconds,
                updatedAt,
                loadedAt,
                true);
        }
    }

    public bool ShouldRefresh(TimeSpan refreshInterval, DateTime nowUtc)
    {
        lock (_sync)
        {
            if (!_snapshot.IsInitialized)
            {
                return true;
            }

            return nowUtc - _snapshot.LoadedAt >= refreshInterval;
        }
    }
}
