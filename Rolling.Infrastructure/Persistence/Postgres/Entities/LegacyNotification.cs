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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string GetTitle(string lang) => lang switch
    {
        "ru" => string.IsNullOrEmpty(RuTitle) ? EnTitle : RuTitle,
        "uz" => string.IsNullOrEmpty(UzTitle) ? EnTitle : UzTitle,
        _ => EnTitle
    };

    public string GetBody(string lang) => lang switch
    {
        "ru" => string.IsNullOrEmpty(RuBody) ? EnBody : RuBody,
        "uz" => string.IsNullOrEmpty(UzBody) ? EnBody : UzBody,
        _ => EnBody
    };
}
