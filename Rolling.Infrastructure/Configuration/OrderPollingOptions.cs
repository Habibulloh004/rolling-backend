namespace Rolling.Infrastructure.Configuration;

public sealed class OrderPollingOptions
{
    public const string SectionName = "OrderPolling";

    /// <summary>
    /// Interval in seconds between polling cycles for order status updates.
    /// Default: 10 seconds.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum time in minutes to track an order before removing it from active tracking.
    /// Default: 180 minutes (3 hours).
    /// </summary>
    public int MaxTrackingMinutes { get; set; } = 180;

    /// <summary>
    /// Whether to enable order status polling.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How many days of Poster transactions to fetch per polling cycle.
    /// Default: 2 days.
    /// </summary>
    public int TransactionsLookbackDays { get; set; } = 2;

    /// <summary>
    /// How many days back to load active orders into memory when polling starts.
    /// Default: 1 day.
    /// </summary>
    public int ActiveOrdersLookbackDays { get; set; } = 1;
}
