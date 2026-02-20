namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class TakeawayAutomationSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public int PreparingDelaySeconds { get; set; } = 300;

    public int ReadyForPickupDelaySeconds { get; set; } = 1800;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
