using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class NotificationRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string EnTitle { get; set; } = string.Empty;

    public string EnBody { get; set; } = string.Empty;

    public string RuTitle { get; set; } = string.Empty;

    public string RuBody { get; set; } = string.Empty;

    public string UzTitle { get; set; } = string.Empty;

    public string UzBody { get; set; } = string.Empty;
}
