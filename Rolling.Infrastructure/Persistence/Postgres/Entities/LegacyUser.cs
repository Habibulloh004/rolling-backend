using System.ComponentModel.DataAnnotations;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class PosterUser
{
    [Key]
    public long UserId { get; set; }

    public string? Name { get; set; }

    public string Login { get; set; } = string.Empty;

    public string? RoleName { get; set; }

    public long RoleId { get; set; }

    public long UserType { get; set; }

    public long AccessMask { get; set; }

    public string? LastIn { get; set; }
}
