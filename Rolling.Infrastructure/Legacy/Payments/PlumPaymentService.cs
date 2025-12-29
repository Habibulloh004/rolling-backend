using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;

namespace Rolling.Infrastructure.Payments;

public sealed class PlumPaymentService
{
    private readonly HttpClient _httpClient;
    private readonly PlumOptions _options;
    private readonly ILogger<PlumPaymentService> _logger;

    public PlumPaymentService(
        HttpClient httpClient,
        IOptions<PlumOptions> options,
        ILogger<PlumPaymentService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string DefaultLanguage => _options.Language;

    public async Task<PlumForwardResult> SendAsync(
        HttpMethod method,
        string path,
        IDictionary<string, string?>? query = null,
        JsonElement? body = null,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        var requestUri = BuildUri(path, query);
        using var requestMessage = new HttpRequestMessage(method, requestUri);

        ApplyAuthentication(requestMessage);
        ApplyLanguage(requestMessage, language);

        if (body.HasValue && ShouldIncludeBody(method))
        {
            requestMessage.Content = new StringContent(body.Value.GetRawText(), Encoding.UTF8, "application/json");
        }

        var response = await _httpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PLUM {Method} {Path} responded {StatusCode}", method, path, (int)response.StatusCode);
        }

        return new PlumForwardResult(
            (int)response.StatusCode,
            contentType,
            payload);
    }

    private void ApplyAuthentication(HttpRequestMessage requestMessage)
    {
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogWarning("Plum credentials are missing.");
            return;
        }

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private void ApplyLanguage(HttpRequestMessage requestMessage, string? language)
    {
        var resolved = string.IsNullOrWhiteSpace(language) ? _options.Language : language;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return;
        }

        requestMessage.Headers.Remove("Language");
        requestMessage.Headers.Add("Language", resolved);
    }

    private static bool ShouldIncludeBody(HttpMethod method) =>
        method == HttpMethod.Post ||
        method == HttpMethod.Put ||
        method == HttpMethod.Patch ||
        method == HttpMethod.Delete;

    private static string BuildUri(string path, IDictionary<string, string?>? query)
    {
        var builder = new StringBuilder(path.TrimStart('/'));
        var hasQuery = false;

        if (query is not null)
        {
            foreach (var (key, value) in query)
            {
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                builder.Append(hasQuery ? '&' : '?');
                builder.Append(Uri.EscapeDataString(key));
                builder.Append('=');
                builder.Append(Uri.EscapeDataString(value));
                hasQuery = true;
            }
        }

        return builder.ToString();
    }
}

public sealed record PlumForwardResult(int StatusCode, string ContentType, byte[] Body);
