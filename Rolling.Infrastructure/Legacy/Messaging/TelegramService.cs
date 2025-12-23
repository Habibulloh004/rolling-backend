using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;

namespace Rolling.Infrastructure.Messaging;

public sealed class TelegramService
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.ChatId))
        {
            _logger.LogWarning("Telegram credentials are not configured.");
            return;
        }

        var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage";
        var payload = new Dictionary<string, string>
        {
            ["chat_id"] = _options.ChatId,
            ["text"] = message
        };

        var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(payload), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to send Telegram message: {StatusCode}", response.StatusCode);
        }
    }

    public async Task<object?> SendMessageRawAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken) || string.IsNullOrWhiteSpace(_options.ChatId))
        {
            _logger.LogWarning("Telegram credentials are not configured.");
            return null;
        }

        var url = $"https://api.telegram.org/bot{_options.BotToken}/sendMessage?chat_id={_options.ChatId}&text={Uri.EscapeDataString(message)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to send Telegram message: {StatusCode}", response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<object>(cancellationToken: cancellationToken);
    }
}
