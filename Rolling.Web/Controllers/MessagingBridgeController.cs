using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Messaging;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class MessagingBridgeController : ControllerBase
{
    private readonly TelegramService _telegramService;

    public MessagingBridgeController(TelegramService telegramService)
    {
        _telegramService = telegramService;
    }

    [HttpGet("sendMessageToTelegram")]
    public async Task<IActionResult> SendMessageAsync([FromQuery] string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest(new { error = "message is required" });
        }

        var response = await _telegramService.SendMessageRawAsync(message, cancellationToken);
        return Ok(response ?? new { success = true });
    }
}
