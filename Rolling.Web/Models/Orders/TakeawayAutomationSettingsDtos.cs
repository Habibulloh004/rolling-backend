namespace Rolling.Web.Models.Orders;

public sealed class TakeawayAutomationSettingsResponse
{
    public int PreparingDelaySeconds { get; init; }

    public int ReadyForPickupDelaySeconds { get; init; }

    public DateTime UpdatedAt { get; init; }
}

public sealed class UpdateTakeawayAutomationSettingsRequest
{
    public int PreparingDelaySeconds { get; init; }

    public int ReadyForPickupDelaySeconds { get; init; }
}
