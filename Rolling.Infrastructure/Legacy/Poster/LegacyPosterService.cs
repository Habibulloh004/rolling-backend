using System.Collections.Generic;
using System.Net;
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

    public sealed record PosterApiCallResult(
        bool Success,
        HttpStatusCode StatusCode,
        string Body,
        JsonDocument? Document);

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
        var result = await SendPosterRequestDetailedAsync(method, query, content, cancellationToken);
        if (result is null)
        {
            return null;
        }

        if (!result.Success)
        {
            result.Document?.Dispose();
            return null;
        }

        return result.Document;
    }

    private async Task<PosterApiCallResult?> SendPosterRequestDetailedAsync(
        string method,
        IDictionary<string, string?>? query,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        if (!IsPosterConfigured())
        {
            _logger.LogWarning("Poster API not configured - ApiBaseUrl: {ApiBaseUrl}, Token length: {TokenLength}",
                _options.ApiBaseUrl, _options.Token?.Length ?? 0);
            return null;
        }

        var url = BuildPosterUrl(method, query);
        _logger.LogInformation("SendPosterRequestAsync - URL: {Url}", url);

        using var requestContent = content;
        var response = content is null
            ? await _httpClient.GetAsync(url, cancellationToken)
            : await _httpClient.PostAsync(url, content, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("SendPosterRequestAsync - Response status: {StatusCode}", response.StatusCode);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("SendPosterRequestAsync - Response body length: {Length}", responseBody.Length);
        }

        JsonDocument? parsed = null;
        try
        {
            parsed = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody);
        }
        catch (JsonException jsonException)
        {
            _logger.LogWarning(jsonException, "Poster API call '{Method}' returned non-JSON body.", method);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster API call '{Method}' failed: {StatusCode}, Response: {Response}",
                method, response.StatusCode, responseBody);
            return new PosterApiCallResult(false, response.StatusCode, responseBody, parsed);
        }

        return new PosterApiCallResult(true, response.StatusCode, responseBody, parsed);
    }

    public async Task<JsonDocument?> CreateIncomingOrderAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateIncomingOrderAsync - Preparing incoming order payload");

        // Poster API expects form-urlencoded data, not JSON
        var formData = ConvertJsonToFormData(payload);

        _logger.LogInformation("CreateIncomingOrderAsync - Form data fields count: {Count}", formData.Count);
        foreach (var kvp in formData)
        {
            _logger.LogInformation("CreateIncomingOrderAsync - Form field: {Key} = {Value}", kvp.Key, kvp.Value);
        }

        var content = new FormUrlEncodedContent(formData);
        var formString = await content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("CreateIncomingOrderAsync - Encoded form data: {FormData}", formString);

        return await SendPosterRequestAsync("incomingOrders.createIncomingOrder", null, content, cancellationToken);
    }

    public async Task<PosterApiCallResult?> CreateIncomingOrderDetailedAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateIncomingOrderDetailedAsync - Preparing incoming order payload");

        var formData = ConvertJsonToFormData(payload);

        _logger.LogInformation("CreateIncomingOrderDetailedAsync - Form data fields count: {Count}", formData.Count);
        foreach (var kvp in formData)
        {
            _logger.LogInformation("CreateIncomingOrderDetailedAsync - Form field: {Key} = {Value}", kvp.Key, kvp.Value);
        }

        var content = new FormUrlEncodedContent(formData);
        var formString = await content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("CreateIncomingOrderDetailedAsync - Encoded form data: {FormData}", formString);

        return await SendPosterRequestDetailedAsync("incomingOrders.createIncomingOrder", null, content, cancellationToken);
    }

    // Helper to extract string value from JsonElement regardless of type (string, number, etc.)
    private static string GetStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetRawText().Trim('"')
        };
    }

    private static bool TryGetDecimalValue(JsonElement element, out decimal value)
    {
        value = 0m;

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var numeric))
        {
            value = numeric;
            return true;
        }

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static long ToMinorUnits(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static bool TryGetLoyaltyPointsMajorUnits(JsonElement payload, out decimal value)
    {
        value = 0m;

        if (payload.TryGetProperty("loyalty_points_used", out var loyaltyPointsUsed) &&
            TryGetDecimalValue(loyaltyPointsUsed, out value) &&
            value > 0m)
        {
            return true;
        }

        if (payload.TryGetProperty("loyaltyPointsUsed", out loyaltyPointsUsed) &&
            TryGetDecimalValue(loyaltyPointsUsed, out value) &&
            value > 0m)
        {
            return true;
        }

        return false;
    }

    private static List<KeyValuePair<string, string>> ConvertJsonToFormData(JsonElement payload)
    {
        var formData = new List<KeyValuePair<string, string>>();

        if (payload.TryGetProperty("spot_id", out var spotId))
            formData.Add(new("spot_id", GetStringValue(spotId)));

        if (payload.TryGetProperty("phone", out var phone))
            formData.Add(new("phone", phone.GetString() ?? ""));

        if (payload.TryGetProperty("first_name", out var firstName) && firstName.ValueKind == JsonValueKind.String)
            formData.Add(new("first_name", firstName.GetString() ?? ""));

        if (payload.TryGetProperty("last_name", out var lastName) && lastName.ValueKind == JsonValueKind.String)
            formData.Add(new("last_name", lastName.GetString() ?? ""));

        if (payload.TryGetProperty("address", out var address) && address.ValueKind == JsonValueKind.String)
            formData.Add(new("address", address.GetString() ?? ""));

        if (payload.TryGetProperty("comment", out var comment) && comment.ValueKind == JsonValueKind.String)
            formData.Add(new("comment", comment.GetString() ?? ""));

        if (payload.TryGetProperty("bonus", out var bonus) &&
            TryGetDecimalValue(bonus, out var bonusMinorUnits) &&
            bonusMinorUnits > 0m)
        {
            // "bonus" is expected in minor units by Poster.
            formData.Add(new("bonus", ((long)Math.Round(bonusMinorUnits, MidpointRounding.AwayFromZero)).ToString()));
        }
        else if (TryGetLoyaltyPointsMajorUnits(payload, out var loyaltyMajorUnits))
        {
            // App payload keeps loyalty points in major units (UZS), Poster expects minor units.
            formData.Add(new("bonus", ToMinorUnits(loyaltyMajorUnits).ToString()));
        }

        var normalizedServiceMode = 3;
        if (payload.TryGetProperty("service_mode", out var serviceMode))
        {
            if (serviceMode.ValueKind == JsonValueKind.Number && serviceMode.TryGetInt32(out var numeric))
            {
                normalizedServiceMode = numeric;
            }
            else if (serviceMode.ValueKind == JsonValueKind.String && int.TryParse(serviceMode.GetString(), out var parsed))
            {
                normalizedServiceMode = parsed;
            }
        }

        formData.Add(new("service_mode", normalizedServiceMode.ToString()));

        // Delivery fee should be passed only for delivery orders.
        if (normalizedServiceMode == 3 && payload.TryGetProperty("delivery_price", out var deliveryPrice))
        {
            if (deliveryPrice.ValueKind == JsonValueKind.Number && deliveryPrice.TryGetInt64(out var numeric) && numeric > 0)
            {
                formData.Add(new("delivery_price", numeric.ToString()));
            }
            else if (deliveryPrice.ValueKind == JsonValueKind.String &&
                     long.TryParse(deliveryPrice.GetString(), out var parsed) &&
                     parsed > 0)
            {
                formData.Add(new("delivery_price", parsed.ToString()));
            }
        }

        // Products array
        if (payload.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var product in products.EnumerateArray())
            {
                if (product.TryGetProperty("product_id", out var productId))
                    formData.Add(new($"products[{index}][product_id]", GetStringValue(productId)));

                if (product.TryGetProperty("count", out var count))
                    formData.Add(new($"products[{index}][count]", count.GetInt32().ToString()));

                if (product.TryGetProperty("modificator_id", out var modId) && modId.ValueKind == JsonValueKind.String)
                {
                    var modIdStr = modId.GetString();
                    if (!string.IsNullOrEmpty(modIdStr))
                        formData.Add(new($"products[{index}][modificator_id]", modIdStr));
                }

                index++;
            }
        }

        // Promotions array
        if (payload.TryGetProperty("promotions", out var promotions) && promotions.ValueKind == JsonValueKind.Array)
        {
            var promoIndex = 0;
            foreach (var promo in promotions.EnumerateArray())
            {
                if (promo.TryGetProperty("id", out var promoId))
                    formData.Add(new($"promotion[{promoIndex}][id]", GetStringValue(promoId)));

                if (promo.TryGetProperty("involved_products", out var involved) && involved.ValueKind == JsonValueKind.Array)
                {
                    var involvedIndex = 0;
                    foreach (var inv in involved.EnumerateArray())
                    {
                        if (inv.TryGetProperty("id", out var invId))
                            formData.Add(new($"promotion[{promoIndex}][involved_products][{involvedIndex}][id]", GetStringValue(invId)));
                        if (inv.TryGetProperty("count", out var invCount))
                            formData.Add(new($"promotion[{promoIndex}][involved_products][{involvedIndex}][count]", invCount.GetInt32().ToString()));
                        if (inv.TryGetProperty("modification_id", out var invMod) && invMod.ValueKind == JsonValueKind.String)
                        {
                            var modStr = invMod.GetString();
                            if (!string.IsNullOrEmpty(modStr))
                                formData.Add(new($"promotion[{promoIndex}][involved_products][{involvedIndex}][modification]", modStr));
                        }
                        involvedIndex++;
                    }
                }

                if (promo.TryGetProperty("result_products", out var result) && result.ValueKind == JsonValueKind.Array)
                {
                    var resultIndex = 0;
                    foreach (var res in result.EnumerateArray())
                    {
                        if (res.TryGetProperty("id", out var resId))
                            formData.Add(new($"promotion[{promoIndex}][result_products][{resultIndex}][id]", GetStringValue(resId)));
                        if (res.TryGetProperty("count", out var resCount))
                            formData.Add(new($"promotion[{promoIndex}][result_products][{resultIndex}][count]", resCount.GetInt32().ToString()));
                        if (res.TryGetProperty("modification_id", out var resMod) && resMod.ValueKind == JsonValueKind.String)
                        {
                            var modStr = resMod.GetString();
                            if (!string.IsNullOrEmpty(modStr))
                                formData.Add(new($"promotion[{promoIndex}][result_products][{resultIndex}][modification]", modStr));
                        }
                        resultIndex++;
                    }
                }

                promoIndex++;
            }
        }

        return formData;
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

    public Task<JsonDocument?> GetPromotionsAsync(
        IDictionary<string, string?>? queryParams,
        CancellationToken cancellationToken) =>
        SendPosterRequestAsync("clients.getPromotions", queryParams, null, cancellationToken);

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
            ["include_delivery"] = includeDelivery ? "1" : "0",
            ["include_products"] = includeProducts ? "1" : "0",
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
            ["include_delivery"] = includeDelivery ? "1" : "0",
            ["include_history"] = includeHistory ? "true" : "false",
            ["include_products"] = includeProducts ? "1" : "0"
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
        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl) || string.IsNullOrWhiteSpace(_options.Token))
        {
            _logger.LogWarning("Poster API configuration is missing.");
            return null;
        }

        var url = $"{_options.ApiBaseUrl}/api/spots.getSpots?token={_options.Token}";
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
        if (string.IsNullOrWhiteSpace(_options.ApiBaseUrl) || string.IsNullOrWhiteSpace(_options.Token))
        {
            _logger.LogWarning("Poster API configuration is missing.");
            return null;
        }

        var url = $"{_options.ApiBaseUrl}/api/access.getEmployees?token={_options.Token}";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Poster employees call failed: {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
    }

    /// <summary>
    /// Get incoming order details by ID to check status.
    /// </summary>
    public Task<JsonDocument?> GetIncomingOrderAsync(string incomingOrderId, CancellationToken cancellationToken) =>
        SendPosterRequestAsync(
            "incomingOrders.getIncomingOrder",
            new Dictionary<string, string?> { ["incoming_order_id"] = incomingOrderId },
            null,
            cancellationToken);

    /// <summary>
    /// Get list of incoming orders with optional filters.
    /// </summary>
    public Task<JsonDocument?> GetIncomingOrdersAsync(
        string? status = null,
        string? dateFrom = null,
        string? dateTo = null,
        CancellationToken cancellationToken = default)
    {
        var query = new Dictionary<string, string?>();

        if (!string.IsNullOrWhiteSpace(status))
            query["status"] = status;
        if (!string.IsNullOrWhiteSpace(dateFrom))
            query["dateFrom"] = dateFrom;
        if (!string.IsNullOrWhiteSpace(dateTo))
            query["dateTo"] = dateTo;

        return SendPosterRequestAsync(
            "incomingOrders.getIncomingOrders",
            query.Count > 0 ? query : null,
            null,
            cancellationToken);
    }

}
