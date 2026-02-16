using System.ComponentModel.DataAnnotations;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class AdminRefreshToken
{
    [Key]
    public long Id { get; set; }

    public long AdminCredentialId { get; set; }

    [Required]
    [MaxLength(256)]
    public string TokenHash { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? ReplacedByTokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public AdminCredential AdminCredential { get; set; } = null!;
}
