using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Rolling.Infrastructure.Configuration;

namespace Rolling.Infrastructure.Poster;

public sealed class PosterService
{
    private readonly HttpClient _httpClient;
    private readonly PosterOptions _options;
    private readonly ILogger<PosterService> _logger;

    public PosterService(HttpClient httpClient, IOptions<PosterOptions> options, ILogger<PosterService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private bool IsPosterConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_options.ApiBaseUrl) && !string.IsNullOrWhiteSpace(_options.Token))
        {
            return true;
        }

        _logger.LogWarning("Poster API configuration is missing.");
        return false;
    }

    private string BuildPosterUrl(string method, IDictionary<string, string?>? query = null)
    {
        var builder = new StringBuilder($"{_options.ApiBaseUrl!.TrimEnd('/')}/api/{method}?token={_options.Token}");
        if (query is null)
        {
            return builder.ToString();
        }

        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            builder.Append('&')
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }

    private async Task<JsonDocument?> SendPosterRequestAsync(string method, IDictionary<string, string?>? query, HttpContent? content, CancellationToken cancellationToken)
    {
        if (!IsPosterConfigured())
        {
            return null;
        }

        var url = BuildPosterUrl(method, query);
        using var requestContent = content;
        var response = content is null
            ? await _httpClient.GetAsync(url, cancellationToken)
            : await _httpClient.PostAsync(url, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster API call '{Method}' failed: {StatusCode}", method, response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(payload) ? "{}" : payload);
    }

    public async Task<JsonDocument?> CreateIncomingOrderAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json");
        return await SendPosterRequestAsync("incomingOrders.createIncomingOrder", null, content, cancellationToken);
    }

    public async Task<JsonDocument?> CreateAbduganiOrderAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.OrdersBackendUrl))
        {
            _logger.LogWarning("Orders backend url is not configured.");
            return null;
        }

        var response = await _httpClient.PostAsJsonAsync(_options.OrdersBackendUrl.TrimEnd('/') + "/add_order", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Failed to create Abdugani order: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    private async Task<JsonDocument?> PostJsonAsync(string url, JsonElement payload, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsJsonAsync(url, payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster API call failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    public Task<JsonDocument?> GetClientGroupsAsync(CancellationToken cancellationToken) =>
        SendPosterRequestAsync("clients.getGroups", null, null, cancellationToken);

    public Task<JsonDocument?> GetClientAsync(string clientId, CancellationToken cancellationToken) =>
        SendPosterRequestAsync(
            "clients.getClient",
            new Dictionary<string, string?> { ["client_id"] = clientId },
            null,
            cancellationToken);

    public Task<JsonDocument?> GetClientsAsync(string? phone, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(phone)
            ? null
            : new Dictionary<string, string?> { ["phone"] = phone };

        return SendPosterRequestAsync("clients.getClients", query, null, cancellationToken);
    }

    public Task<JsonDocument?> CreateClientAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var content = new StringContent(payload.GetRawText(), Encoding.UTF8, "application/json");
        return SendPosterRequestAsync("clients.createClient", null, content, cancellationToken);
    }

    public Task<JsonDocument?> GetProductsAsync(CancellationToken cancellationToken) =>
        SendPosterRequestAsync("menu.getProducts", null, null, cancellationToken);

    public Task<JsonDocument?> GetCategoriesAsync(CancellationToken cancellationToken) =>
        SendPosterRequestAsync("menu.getCategories", null, null, cancellationToken);

    public Task<JsonDocument?> GetTransactionsAsync(
        string courierId,
        string dateFrom,
        string dateTo,
        CancellationToken cancellationToken,
        bool includeDelivery = true,
        bool includeProducts = true,
        bool includeHistory = true)
    {
        var query = new Dictionary<string, string?>
        {
            ["courier_id"] = courierId,
            ["dateFrom"] = dateFrom,
            ["dateTo"] = dateTo,
            ["include_delivery"] = includeDelivery ? "true" : "false",
            ["include_products"] = includeProducts ? "true" : "false",
            ["include_history"] = includeHistory ? "true" : "false"
        };

        return SendPosterRequestAsync("dash.getTransactions", query, null, cancellationToken);
    }

    public Task<JsonDocument?> GetTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken,
        bool includeDelivery = true,
        bool includeHistory = true,
        bool includeProducts = true)
    {
        var query = new Dictionary<string, string?>
        {
            ["transaction_id"] = transactionId,
            ["include_delivery"] = includeDelivery ? "true" : "false",
            ["include_history"] = includeHistory ? "true" : "false",
            ["include_products"] = includeProducts ? "true" : "false"
        };

        return SendPosterRequestAsync("dash.getTransaction", query, null, cancellationToken);
    }

    public Task<JsonDocument?> GetTransactionProductsAsync(string transactionId, CancellationToken cancellationToken) =>
        SendPosterRequestAsync(
            "dash.getTransactionProducts",
            new Dictionary<string, string?> { ["transaction_id"] = transactionId },
            null,
            cancellationToken);

    public async Task<JsonDocument?> GetSpotsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SpotsBaseUrl) || string.IsNullOrWhiteSpace(_options.SpotsToken))
        {
            _logger.LogWarning("Poster spots configuration is missing.");
            return null;
        }

        var url = $"{_options.SpotsBaseUrl}{_options.SpotsToken}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster spots call failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    public async Task<JsonDocument?> GetEmployeesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.EmployeeBaseUrl))
        {
            _logger.LogWarning("Poster employee endpoint is not configured.");
            return null;
        }

        var url = $"{_options.EmployeeBaseUrl}{_options.Token}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster employees call failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    public async Task<JsonDocument?> GetAlternateOrderAsync(string transactionComment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AlternateOrdersUrl) || string.IsNullOrWhiteSpace(transactionComment))
        {
            return null;
        }

        var url = $"{_options.AlternateOrdersUrl.TrimEnd('/')}/get_order/{Uri.EscapeDataString(transactionComment)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Alternate orders call failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }
}
