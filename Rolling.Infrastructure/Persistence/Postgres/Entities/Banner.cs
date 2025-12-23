using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Rolling.Infrastructure.Persistence.Postgres.Entities;

public sealed class Banner
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Image URL (e.g., http://localhost:5020/uploads/image.jpg)
    /// </summary>
    public string? ImageUrl { get; set; }

    public string Lang { get; set; } = "ru";

    public string? Path { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}