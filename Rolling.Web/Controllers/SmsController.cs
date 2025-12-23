using Microsoft.AspNetCore.Mvc;
using Rolling.Web.Models.Sms;
using Rolling.Web.Services.Sms;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/sms")]
public sealed class SmsController : ControllerBase
{
    private readonly ISmsService _smsService;
    private readonly EskizSmsClient _smsClient;

    public SmsController(ISmsService smsService, EskizSmsClient smsClient)
    {
        _smsService = smsService;
        _smsClient = smsClient;
    }

    [HttpGet("token")]
    public async Task<IActionResult> GetTokenAsync(CancellationToken cancellationToken)
    {
        var token = await _smsClient.GetTokenAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = "Unable to obtain Eskiz token" });
        }

        return Ok(new { token });
    }

    [HttpPost("login/request")]
    public async Task<IActionResult> RequestLoginCodeAsync([FromBody] SmsLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await _smsService.RequestLoginCodeAsync(request.Phone, cancellationToken);
        if (!result.Success)
        {
            return BadRequest(new
            {
                status = "error",
                code = result.Code,
                message = result.Message
            });
        }

        return Ok(new
        {
            status = "success",
            code = result.Code,
            message = result.Message,
            expiresAt = result.ExpiresAt
        });
    }

    [HttpPost("login/verify")]
    public IActionResult VerifyLoginCodeAsync([FromBody] SmsVerifyCodeRequest request)
    {
        var result = _smsService.VerifyLoginCode(request.Phone, request.Code);
        if (!result.Success)
        {
            return BadRequest(new
            {
                status = "error",
                code = result.Code,
                message = result.Message,
                expired = result.Expired
            });
        }

        return Ok(new
        {
            status = "success",
            code = result.Code,
            message = result.Message
        });
    }
}
