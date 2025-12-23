using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Web.Models.Sms;

namespace Rolling.Web.Services.Sms;

public sealed class EskizSmsClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<SmsOptions> _optionsMonitor;
    private readonly ILogger<EskizSmsClient> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public EskizSmsClient(
        HttpClient httpClient,
        IOptionsMonitor<SmsOptions> optionsMonitor,
        ILogger<EskizSmsClient> logger,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(_cachedToken) && now < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (!string.IsNullOrWhiteSpace(_cachedToken) && now < _tokenExpiresAt)
            {
                return _cachedToken;
            }

            var token = await RequestTokenAsync(cancellationToken);
            if (token is null)
            {
                return null;
            }

            var lifetimeMinutes = Math.Max(1, _optionsMonitor.CurrentValue.Eskiz.TokenLifetimeMinutes);
            _cachedToken = token;
            _tokenExpiresAt = now.AddMinutes(lifetimeMinutes);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    public void InvalidateToken()
    {
        _cachedToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;
    }

    public async Task<SmsSendResult> SendSmsAsync(string phone, string message, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                return new SmsSendResult(false, null, "Unable to obtain Eskiz token");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "message/sms/send")
            {
                Content = new FormUrlEncodedContent(BuildSmsPayload(phone, message))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("📨 Eskiz SMS delivered to {Phone}", phone);
                return new SmsSendResult(true, body, null);
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                _logger.LogWarning("Eskiz token expired, refreshing");
                InvalidateToken();
                continue;
            }

            _logger.LogError(
                "Eskiz SMS failed for {Phone}. Status: {Status}. Body: {Body}",
                phone,
                response.StatusCode,
                body);

            return new SmsSendResult(false, body, $"Eskiz rejected SMS with status {(int)response.StatusCode}");
        }

        return new SmsSendResult(false, null, "Unable to send SMS after refreshing token");
    }

    private Dictionary<string, string> BuildSmsPayload(string phone, string message)
    {
        var options = _optionsMonitor.CurrentValue.Eskiz;
        var payload = new Dictionary<string, string>
        {
            ["mobile_phone"] = phone,
            ["message"] = message,
            ["from"] = options.Sender
        };

        if (!string.IsNullOrWhiteSpace(options.CallbackUrl))
        {
            payload["callback_url"] = options.CallbackUrl!;
        }

        return payload;
    }

    private async Task<string?> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue.Eskiz;
        if (string.IsNullOrWhiteSpace(options.Credentials.Email) || string.IsNullOrWhiteSpace(options.Credentials.Password))
        {
            _logger.LogError("Eskiz credentials are missing in configuration");
            return null;
        }

        var payload = new Dictionary<string, string>
        {
            ["email"] = options.Credentials.Email,
            ["password"] = options.Credentials.Password
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/login")
        {
            Content = new FormUrlEncodedContent(payload)
        };

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Unable to authenticate with Eskiz. Status: {Status}. Body: {Body}",
                response.StatusCode,
                body);
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("data", out var dataElement) &&
                dataElement.TryGetProperty("token", out var tokenElement))
            {
                var token = tokenElement.GetString();
                _logger.LogInformation("Eskiz token refreshed");
                return token;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Eskiz token response: {Body}", body);
        }

        _logger.LogError("Eskiz token response missing token field: {Body}", body);
        return null;
    }
}

public sealed record SmsSendResult(bool Success, string? ProviderResponse, string? ErrorMessage);
