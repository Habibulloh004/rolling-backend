using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Rolling.Infrastructure.Poster;

namespace Rolling.Web.Controllers;

[ApiController]
[Route("api/client")]
public sealed class ClientAuthController : ControllerBase
{
    private readonly CachedPosterService _cachedPosterService;
    private readonly ILogger<ClientAuthController> _logger;

    public ClientAuthController(
        CachedPosterService cachedPosterService,
        ILogger<ClientAuthController> logger)
    {
        _cachedPosterService = cachedPosterService;
        _logger = logger;
    }

    /// <summary>
    /// Client login with phone and password
    /// POST /api/client/login
    /// Password is stored in Poster client comment field as JSON: {"password": "..."}
    /// iOS app handles registration/password setting directly to Poster
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] ClientLoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Phone))
            return BadRequest(new { error = "Phone is required" });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Password is required" });

        var normalizedPhone = NormalizePhone(request.Phone);

        // Find client by phone
        var client = await FindClientByPhoneAsync(normalizedPhone, cancellationToken);
        if (client == null)
            return NotFound(new { error = "Client not found" });

        var clientId = client.Value.GetProperty("client_id").ToString();
        var comment = client.Value.TryGetProperty("comment", out var commentProp)
            ? commentProp.GetString() ?? ""
            : "";

        // Parse credentials from comment field
        var credentials = ParseCredentials(comment);
        if (credentials == null || string.IsNullOrEmpty(credentials.Password))
            return Unauthorized(new { error = "Password not set. Please register first." });

        // Verify password (plain text comparison)
        if (credentials.Password != request.Password)
            return Unauthorized(new { error = "Invalid password" });

        _logger.LogInformation("Client {ClientId} logged in successfully", clientId);

        return Ok(new
        {
            success = true,
            client = MapClientResponse(client.Value)
        });
    }

    /// <summary>
    /// Verify if client has password set
    /// GET /api/client/check-password?phone=998...
    /// </summary>
    [HttpGet("check-password")]
    public async Task<IActionResult> CheckPasswordAsync([FromQuery] string phone, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return BadRequest(new { error = "Phone is required" });

        var normalizedPhone = NormalizePhone(phone);

        var client = await FindClientByPhoneAsync(normalizedPhone, cancellationToken);
        if (client == null)
            return Ok(new { exists = false, hasPassword = false });

        var comment = client.Value.TryGetProperty("comment", out var commentProp)
            ? commentProp.GetString() ?? ""
            : "";

        var credentials = ParseCredentials(comment);
        var hasPassword = credentials != null && !string.IsNullOrEmpty(credentials.Password);

        return Ok(new
        {
            exists = true,
            hasPassword = hasPassword,
            client_id = client.Value.GetProperty("client_id").ToString()
        });
    }

    private async Task<JsonElement?> FindClientByPhoneAsync(string phone, CancellationToken cancellationToken)
    {
        var result = await _cachedPosterService.GetClientsAsync(
            new Dictionary<string, string?> { ["phone"] = phone },
            cancellationToken);

        if (result?.Data == null)
            return null;

        if (!result.Data.RootElement.TryGetProperty("response", out var response))
            return null;

        if (response.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var client in response.EnumerateArray())
        {
            if (client.TryGetProperty("phone", out var phoneProp))
            {
                var clientPhone = NormalizePhone(phoneProp.GetString() ?? "");
                if (clientPhone == phone)
                    return client;
            }
        }

        return null;
    }

    private static string NormalizePhone(string phone)
    {
        // Remove all non-digit characters
        var digits = new string(phone.Where(char.IsDigit).ToArray());

        // Ensure it starts with country code
        if (digits.StartsWith("998"))
            return digits;

        if (digits.Length == 9)
            return "998" + digits;

        return digits;
    }

    private static ClientCredentials? ParseCredentials(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ClientCredentials>(comment);
        }
        catch
        {
            return null;
        }
    }

    private static object MapClientResponse(JsonElement client)
    {
        string? GetString(string prop) =>
            client.TryGetProperty(prop, out var val) ? val.GetString() : null;

        string? GetNumber(string prop) =>
            client.TryGetProperty(prop, out var val) ? val.ToString() : null;

        return new
        {
            client_id = GetNumber("client_id"),
            client_name = GetString("client_name"),
            phone = GetString("phone"),
            bonus = GetNumber("bonus"),
            total_payed_sum = GetNumber("total_payed_sum"),
            client_groups_id = GetNumber("client_groups_id"),
            birthday = GetString("birthday"),
            client_sex = GetNumber("client_sex")
        };
    }
}

public sealed class ClientLoginRequest
{
    public string? Phone { get; init; }
    public string? Password { get; init; }
}

/// <summary>
/// Credentials stored in Poster client comment field as JSON
/// iOS app sets this directly via Poster API
/// </summary>
public sealed class ClientCredentials
{
    public string? Password { get; set; }
}
