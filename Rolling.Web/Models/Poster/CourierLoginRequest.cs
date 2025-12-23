using System.ComponentModel.DataAnnotations;

namespace Rolling.Web.Models.Poster;

public sealed class CourierLoginRequest
{
    [Required]
    [EmailAddress]
    public string? Email { get; init; }
}
