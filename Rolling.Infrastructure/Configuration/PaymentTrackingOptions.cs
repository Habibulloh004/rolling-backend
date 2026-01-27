namespace Rolling.Infrastructure.Configuration;

public sealed class PaymentTrackingOptions
{
    public const string SectionName = "PaymentTracking";

    public int SweepIntervalSeconds { get; set; } = 60;

    public int ExpirationMinutes { get; set; } = 15;

    public int HydrationIntervalSeconds { get; set; } = 300;

    public int ActivePaymentsLookbackMinutes { get; set; } = 180;

    public int MaxTrackingMinutes { get; set; } = 180;

    public bool Enabled { get; set; } = true;
}
