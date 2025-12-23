using System.ComponentModel.DataAnnotations;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class PaymentTransaction
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string? TransactionId { get; set; }

    public string? UserId { get; set; }

    public string? OrderDetailsJson { get; set; }

    public int Status { get; set; }

    public decimal Amount { get; set; }

    public string? OrderId { get; set; }

    public long CreateTime { get; set; }

    public long PerformTime { get; set; }

    public long CancelTime { get; set; }

    public int? Reason { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string? PrepareId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
