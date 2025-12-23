using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Poster;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("")]
public sealed class PosterBridgeController : ControllerBase
{
    private readonly PosterService _posterService;

    public PosterBridgeController(PosterService posterService)
    {
        _posterService = posterService;
    }

    [HttpGet("posterClientGroup")]
    public async Task<IActionResult> GetClientGroupsAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetClientGroupsAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("posterClient/{id}")]
    public async Task<IActionResult> GetClientAsync(string id, CancellationToken cancellationToken)
    {
        var document = await _posterService.GetClientAsync(id, cancellationToken);
        return CreatePosterResponse(document, unwrapResponse: false);
    }

    [HttpPost("posterCreateClient")]
    public async Task<IActionResult> CreateClientAsync([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var document = await _posterService.CreateClientAsync(payload, cancellationToken);
        return CreatePosterResponse(document, unwrapResponse: false);
    }

    [HttpGet("posterClients")]
    public async Task<IActionResult> GetClientsAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetClientsAsync(null, cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("posterProducts")]
    public async Task<IActionResult> GetProductsAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetProductsAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("posterCategories")]
    public async Task<IActionResult> GetCategoriesAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetCategoriesAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpGet("getClientTransaction/{phone}")]
    public async Task<IActionResult> GetClientByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var convertedPhone = ConvertPhoneNumber(phone);
        var document = await _posterService.GetClientsAsync(convertedPhone, cancellationToken);
        if (document is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Poster service unavailable" });
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("response", out var clients) || clients.ValueKind != JsonValueKind.Array)
            {
                return Ok(new { error = "No client found." });
            }

            var first = clients.EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Undefined
                ? Ok(new { error = "No client found." })
                : Ok(first.Clone());
        }
    }

    [HttpGet("getSpot")]
    public async Task<IActionResult> GetSpotAsync(CancellationToken cancellationToken)
    {
        var document = await _posterService.GetSpotsAsync(cancellationToken);
        return CreatePosterResponse(document);
    }

    [HttpPost("api/posttoposter")]
    public async Task<IActionResult> PostToPosterAsync([FromBody] JsonElement payload, CancellationToken cancellationToken)
    {
        var document = await _posterService.CreateIncomingOrderAsync(payload, cancellationToken);
        return CreatePosterResponse(document, unwrapResponse: false);
    }

    private IActionResult CreatePosterResponse(JsonDocument? document, bool unwrapResponse = true)
    {
        if (document is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Poster service unavailable" });
        }

        using (document)
        {
            var root = document.RootElement;
            if (unwrapResponse && root.TryGetProperty("response", out var response))
            {
                return Ok(response.Clone());
            }

            return Ok(root.Clone());
        }
    }

    private static string ConvertPhoneNumber(string phoneNumber)
    {
        var normalized = phoneNumber.Trim();
        if (normalized.StartsWith("+", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        if (!normalized.StartsWith("998", StringComparison.Ordinal) && normalized.Length < 12)
        {
            normalized = $"998{normalized}";
        }

        return normalized;
    }
}
