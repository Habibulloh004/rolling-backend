using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Orders;

/// <summary>
/// Tracks active takeaway orders in memory to avoid heavy database scans.
/// </summary>
public sealed class TakeawayOrderTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _activeTakeawayOrders = new();
    private readonly ILogger<TakeawayOrderTracker> _logger;

    public TakeawayOrderTracker(ILogger<TakeawayOrderTracker> logger)
    {
        _logger = logger;
    }

    public void TrackOrder(Order order)
    {
        if (order.ServiceMode != 2)
        {
            return;
        }

        TrackOrder(order.Id);
    }

    public void TrackOrder(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return;
        }

        if (_activeTakeawayOrders.TryAdd(orderId, DateTime.UtcNow))
        {
            _logger.LogInformation(
                "Started tracking takeaway order {OrderId}",
                orderId);
            return;
        }

        _activeTakeawayOrders[orderId] = DateTime.UtcNow;
    }

    public bool UntrackOrder(string orderId)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return false;
        }

        var removed = _activeTakeawayOrders.TryRemove(orderId, out _);
        if (removed)
        {
            _logger.LogInformation(
                "Stopped tracking takeaway order {OrderId}",
                orderId);
        }

        return removed;
    }

    public bool IsTracked(string orderId) => _activeTakeawayOrders.ContainsKey(orderId);

    public IReadOnlyCollection<string> GetTrackedOrderIds() =>
        _activeTakeawayOrders.Keys.ToList().AsReadOnly();

    public int TrackedCount => _activeTakeawayOrders.Count;
}
