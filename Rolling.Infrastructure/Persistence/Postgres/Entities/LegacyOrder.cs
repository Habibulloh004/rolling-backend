using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class CourierOrder
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string OrderId { get; set; } = string.Empty;

    public long CourierId { get; set; }

    public string? OrderDataJson { get; set; }

    public string? ProductsJson { get; set; }

    public string Status { get; set; } = "waiting";
}
