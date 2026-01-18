using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Infrastructure.Orders;

/// <summary>
/// Tracks active orders that need status polling from Poster.
/// Orders are added when created and removed when they reach a terminal status.
/// </summary>
public sealed class ActiveOrderTracker
{
    private readonly ConcurrentDictionary<string, TrackedOrder> _activeOrders = new();
    private readonly ILogger<ActiveOrderTracker> _logger;

    public ActiveOrderTracker(ILogger<ActiveOrderTracker> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Add an order to be tracked for status updates.
    /// </summary>
    public void TrackOrder(TrackedOrder order)
    {
        if (string.IsNullOrWhiteSpace(order.OrderId))
        {
            _logger.LogWarning("Cannot track order with empty ID");
            return;
        }

        if (_activeOrders.TryAdd(order.OrderId, order))
        {
            _logger.LogInformation(
                "Started tracking order {OrderId} (Poster ID: {PosterId}, FCM: {HasFcm})",
                order.OrderId,
                order.PosterIncomingOrderId ?? "none",
                !string.IsNullOrWhiteSpace(order.FcmToken));
        }
        else
        {
            _activeOrders[order.OrderId] = order;
            _logger.LogInformation("Updated tracked order {OrderId}", order.OrderId);
        }
    }

    /// <summary>
    /// Remove an order from tracking (e.g., when it reaches terminal status).
    /// </summary>
    public bool UntrackOrder(string orderId)
    {
        if (_activeOrders.TryRemove(orderId, out _))
        {
            _logger.LogInformation("Stopped tracking order {OrderId}", orderId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Get all currently tracked orders.
    /// </summary>
    public IReadOnlyCollection<TrackedOrder> GetTrackedOrders()
    {
        return _activeOrders.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// Get a specific tracked order by ID.
    /// </summary>
    public TrackedOrder? GetTrackedOrder(string orderId)
    {
        return _activeOrders.TryGetValue(orderId, out var order) ? order : null;
    }

    /// <summary>
    /// Update the status of a tracked order after polling Poster.
    /// </summary>
    public bool UpdateOrderStatus(string orderId, OrderStatus newPosterStatus)
    {
        if (!_activeOrders.TryGetValue(orderId, out var order))
        {
            return false;
        }

        var updated = order with
        {
            CurrentStatus = newPosterStatus,
            LastPosterStatus = newPosterStatus,
            LastCheckedAt = DateTime.UtcNow
        };
        _activeOrders[orderId] = updated;

        _logger.LogInformation(
            "Updated tracked order {OrderId} status to {Status}",
            orderId,
            newPosterStatus);

        return true;
    }

    /// <summary>
    /// Record the first poll result without triggering notification.
    /// Sets LastPosterStatus as the baseline for future comparisons.
    /// </summary>
    public bool SetInitialPosterStatus(string orderId, OrderStatus posterStatus)
    {
        if (!_activeOrders.TryGetValue(orderId, out var order))
        {
            return false;
        }

        var updated = order with
        {
            CurrentStatus = posterStatus,
            LastPosterStatus = posterStatus,
            LastCheckedAt = DateTime.UtcNow
        };
        _activeOrders[orderId] = updated;

        _logger.LogInformation(
            "Set initial Poster status for order {OrderId}: {Status}",
            orderId,
            posterStatus);

        return true;
    }

    /// <summary>
    /// Check if an order is being tracked.
    /// </summary>
    public bool IsTracked(string orderId) => _activeOrders.ContainsKey(orderId);

    /// <summary>
    /// Get count of tracked orders.
    /// </summary>
    public int TrackedCount => _activeOrders.Count;

    /// <summary>
    /// Clear all tracked orders (for testing or shutdown).
    /// </summary>
    public void ClearAll()
    {
        _activeOrders.Clear();
        _logger.LogInformation("Cleared all tracked orders");
    }
}

/// <summary>
/// Represents an order being tracked for status updates.
/// </summary>
public sealed record TrackedOrder
{
    /// <summary>
    /// Internal order ID.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// Order number displayed to customer.
    /// </summary>
    public required string OrderNumber { get; init; }

    /// <summary>
    /// Poster incoming order ID (used for polling).
    /// </summary>
    public string? PosterIncomingOrderId { get; init; }

    /// <summary>
    /// Poster transaction ID (alternative for polling).
    /// </summary>
    public string? PosterTransactionId { get; init; }

    /// <summary>
    /// FCM token for sending push notifications.
    /// </summary>
    public string? FcmToken { get; init; }

    /// <summary>
    /// Customer phone number.
    /// </summary>
    public string? Phone { get; init; }

    /// <summary>
    /// Current known status of the order (from our database).
    /// </summary>
    public OrderStatus CurrentStatus { get; init; } = OrderStatus.Pending;

    /// <summary>
    /// Last status received from Poster API.
    /// Null means we haven't polled yet - first poll establishes baseline.
    /// </summary>
    public OrderStatus? LastPosterStatus { get; init; }

    /// <summary>
    /// When the order was added to tracking.
    /// </summary>
    public DateTime TrackedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// When the order status was last checked.
    /// </summary>
    public DateTime? LastCheckedAt { get; init; }

    /// <summary>
    /// Preferred language for notifications (en, ru, uz).
    /// </summary>
    public string Language { get; init; } = "en";
}
