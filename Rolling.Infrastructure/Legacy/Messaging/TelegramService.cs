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

    // Hardcoded Telegram credentials (per user request)
    private const string HardcodedBotToken = "7051935328:AAFJxJAVsRTPxgj3rrHWty1pEUlMkBgg9_o";
    private const string HardcodedChatId = "-1002211902296";

    public TelegramService(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(HardcodedBotToken) || string.IsNullOrWhiteSpace(HardcodedChatId))
        {
            _logger.LogWarning("Telegram credentials are not configured.");
            return;
        }

        var url = $"https://api.telegram.org/bot{HardcodedBotToken}/sendMessage";
        var payload = new Dictionary<string, string>
        {
            ["chat_id"] = HardcodedChatId,
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
        if (string.IsNullOrWhiteSpace(HardcodedBotToken) || string.IsNullOrWhiteSpace(HardcodedChatId))
        {
            _logger.LogWarning("Telegram credentials are not configured.");
            return null;
        }

        var url = $"https://api.telegram.org/bot{HardcodedBotToken}/sendMessage?chat_id={HardcodedChatId}&text={Uri.EscapeDataString(message)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to send Telegram message: {StatusCode}", response.StatusCode);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<object>(cancellationToken: cancellationToken);
    }
}
