using System.ComponentModel.DataAnnotations;

namespace Rolling.Web.Models.Time;

public sealed class TimeUpdateRequest
{
    [Required]
    public string OpenedTime { get; set; } = string.Empty;

    [Required]
    public string ClosedTime { get; set; } = string.Empty;
}
