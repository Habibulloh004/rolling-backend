using System.ComponentModel.DataAnnotations;

namespace Rolling.Web.Models.Sms;

public sealed class SmsLoginCodeRequest
{
    [Required]
    public string Phone { get; set; } = string.Empty;
}

public sealed class SmsVerifyCodeRequest
{
    [Required]
    public string Phone { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;
}
