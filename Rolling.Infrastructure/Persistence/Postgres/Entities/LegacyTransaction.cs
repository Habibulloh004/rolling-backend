using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class PaymentTransaction
{
    [Key]
    public string Id { get; set; } = GenerateMongoStyleId();

    /// <summary>
    /// Generates a 24-character hex string similar to MongoDB ObjectId format.
    /// Format: 8 chars (timestamp) + 16 chars (random) = 24 chars total
    /// </summary>
    private static string GenerateMongoStyleId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampHex = timestamp.ToString("x8");
        var randomBytes = RandomNumberGenerator.GetBytes(8);
        var randomHex = Convert.ToHexString(randomBytes).ToLowerInvariant();
        return timestampHex + randomHex;
    }

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
