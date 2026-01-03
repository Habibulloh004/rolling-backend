using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

/// <summary>
/// Order timeline event entity matching iOS TimelineEvent model structure
/// </summary>
public sealed class OrderTimelineEvent
{
    [Key]
    public string Id { get; set; } = string.Empty;

    // Reference to parent order
    public string OrderId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public DateTime? Time { get; set; }

    public bool IsCompleted { get; set; }

    public bool IsCurrent { get; set; }

    // Order of the timeline event
    public int SortOrder { get; set; }

    // Navigation Property
    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
}
