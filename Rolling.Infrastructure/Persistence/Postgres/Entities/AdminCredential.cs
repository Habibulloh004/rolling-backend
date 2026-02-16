using System.ComponentModel.DataAnnotations;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class AdminCredential
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Login { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    public string PasswordSalt { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? Name { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AdminRefreshToken> RefreshTokens { get; set; } = new List<AdminRefreshToken>();
}
