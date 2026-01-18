using System.Text.Json;
using Rolling.Infrastructure.Persistence.Postgres.Entities;

namespace Rolling.Web.Services.Webhooks;

internal static class PosterTransactionWebhookParser
{
    internal static bool TryParseFromEnvelope(JsonElement payload, out TransactionStatusUpdate update)
    {
        update = new TransactionStatusUpdate(string.Empty, default, string.Empty);

        if (!TryGetString(payload, "object", out var objectType) ||
            !string.Equals(objectType, "transaction", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetString(payload, "object_id", out var transactionId) ||
            string.IsNullOrWhiteSpace(transactionId))
        {
            return false;
        }

        if (!TryGetObject(payload, "data", out var dataObject) &&
            !TryGetEmbeddedObject(payload, "data", out dataObject))
        {
            return false;
        }

        return TryParseFromDataInternal(transactionId, dataObject, out update);
    }

    internal static bool TryParseFromData(string transactionId, JsonElement data, out TransactionStatusUpdate update)
    {
        update = new TransactionStatusUpdate(string.Empty, default, string.Empty);

        if (string.IsNullOrWhiteSpace(transactionId))
        {
            return false;
        }

        if (data.ValueKind == JsonValueKind.String)
        {
            var raw = data.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var trimmed = raw.Trim();
                if (trimmed.StartsWith("{", StringComparison.Ordinal))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(trimmed);
                        return TryParseFromDataInternal(transactionId, document.RootElement, out update);
                    }
                    catch (JsonException)
                    {
                        return false;
                    }
                }
            }
        }

        return TryParseFromDataInternal(transactionId, data, out update);
    }

    private static bool TryParseFromDataInternal(string transactionId, JsonElement data, out TransactionStatusUpdate update)
    {
        update = new TransactionStatusUpdate(string.Empty, default, string.Empty);

        if (!TryGetObject(data, "transactions_history", out var history) &&
            !TryGetEmbeddedObject(data, "transactions_history", out history))
        {
            if (TryGetArray(data, "transactions_history", out var historyArray) ||
                TryGetEmbeddedArray(data, "transactions_history", out historyArray))
            {
                if (!TryGetLatestHistory(historyArray, out history))
                {
                    return false;
                }
            }
            else if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("type_history", out _))
            {
                history = data;
            }
            else
            {
                return false;
            }
        }

        if (!TryGetString(history, "type_history", out var typeHistory))
        {
            return false;
        }

        if (!TryMapStatus(typeHistory, history, out var status))
        {
            return false;
        }

        update = new TransactionStatusUpdate(transactionId, status, typeHistory);
        return true;
    }

    private static bool TryMapStatus(string typeHistory, JsonElement history, out OrderStatus status)
    {
        status = default;

        switch (typeHistory.Trim().ToLowerInvariant())
        {
            case "changeprocessingstatus":
                if (TryGetInt(history, "value", out var processingCode))
                {
                    return TryMapProcessingStatus(processingCode, out status);
                }

                if (TryGetString(history, "value", out var processingValue))
                {
                    return TryMapProcessingStatus(processingValue, out status);
                }

                return false;

            case "changeorderstatus":
                if (TryGetInt(history, "value", out var orderStatusCode))
                {
                    if (TryMapOrderStatusChangeCode(orderStatusCode, out status))
                    {
                        return true;
                    }

                    return TryMapOrderStatusCode(orderStatusCode, out status);
                }

                if (TryGetString(history, "value", out var orderStatusValue))
                {
                    if (TryMapOrderStatusChangeCode(orderStatusValue, out status))
                    {
                        return true;
                    }

                    return TryMapOrderStatusCode(orderStatusValue, out status);
                }

                return false;

            case "settable":
                status = OrderStatus.Accepted;
                return true;

            case "close":
                status = MapCloseStatus(history);
                return true;

            case "open":
                status = OrderStatus.Pending;
                return true;
        }

        return false;
    }

    private static bool TryMapProcessingStatus(int code, out OrderStatus status)
    {
        if (code >= 61)
        {
            status = OrderStatus.Delivered;
            return true;
        }

        switch (code)
        {
            case 0:
            case 10:
                status = OrderStatus.Pending;
                return true;
            case 1:
            case 20:
                status = OrderStatus.Accepted;
                return true;
            case 2:
            case 30:
                status = OrderStatus.Preparing;
                return true;
            case 3:
                status = OrderStatus.Preparing;
                return true;
            case 4:
                status = OrderStatus.OnTheWay;
                return true;
            case 40:
                status = OrderStatus.OnTheWay;
                return true;
            case 5:
            case 50:
                status = OrderStatus.Delivered;
                return true;
            case 6:
            case 60:
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static bool TryMapOrderStatusChangeCode(int code, out OrderStatus status)
    {
        // Poster changeorderstatus codes (webhook history):
        // 1 = accepted, 4 = cancelled
        switch (code)
        {
            case 1:
                status = OrderStatus.Accepted;
                return true;
            case 4:
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static bool TryMapOrderStatusChangeCode(string value, out OrderStatus status)
    {
        if (int.TryParse(value, out var code))
        {
            return TryMapOrderStatusChangeCode(code, out status);
        }

        status = default;
        return false;
    }

    private static bool TryMapProcessingStatus(string value, out OrderStatus status)
    {
        if (int.TryParse(value, out var code))
        {
            return TryMapProcessingStatus(code, out status);
        }

        var normalized = value.Trim().ToLowerInvariant()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        switch (normalized)
        {
            case "new":
            case "pending":
                status = OrderStatus.Pending;
                return true;
            case "accepted":
                status = OrderStatus.Accepted;
                return true;
            case "cooking":
            case "preparing":
            case "ready":
                status = OrderStatus.Preparing;
                return true;
            case "ontheway":
            case "onway":
                status = OrderStatus.OnTheWay;
                return true;
            case "delivered":
            case "completed":
            case "closed":
                status = OrderStatus.Delivered;
                return true;
            case "cancelled":
            case "canceled":
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static bool TryMapOrderStatusCode(int code, out OrderStatus status)
    {
        // Poster order status codes:
        // 0 = new/pending, 1 = accepted, 2 = cooking/preparing, 3 = ready, 4 = on the way, 5 = delivered, 6 = cancelled
        switch (code)
        {
            case 0:
                status = OrderStatus.Pending;
                return true;
            case 1:
                status = OrderStatus.Accepted;
                return true;
            case 2:
                status = OrderStatus.Preparing;
                return true;
            case 3:
                status = OrderStatus.Preparing; // Ready maps to Preparing
                return true;
            case 4:
                status = OrderStatus.OnTheWay;
                return true;
            case 5:
                status = OrderStatus.Delivered;
                return true;
            case 6:
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static bool TryMapOrderStatusCode(string value, out OrderStatus status)
    {
        if (int.TryParse(value, out var code))
        {
            return TryMapOrderStatusCode(code, out status);
        }

        var normalized = value.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "new":
            case "pending":
                status = OrderStatus.Pending;
                return true;
            case "accepted":
                status = OrderStatus.Accepted;
                return true;
            case "cooking":
            case "preparing":
                status = OrderStatus.Preparing;
                return true;
            case "ready":
                status = OrderStatus.Preparing; // Ready maps to Preparing
                return true;
            case "ontheway":
            case "on_the_way":
            case "onway":
            case "on_way":
                status = OrderStatus.OnTheWay;
                return true;
            case "delivered":
            case "completed":
                status = OrderStatus.Delivered;
                return true;
            case "cancelled":
            case "canceled":
                status = OrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private static OrderStatus MapCloseStatus(JsonElement history)
    {
        if (TryGetObject(history, "value_text", out var valueText) ||
            TryGetEmbeddedObject(history, "value_text", out valueText))
        {
            if (TryGetString(valueText, "reason", out var reason))
            {
                return reason switch
                {
                    "1" => OrderStatus.Cancelled,
                    "2" => OrderStatus.Cancelled,
                    "4" => OrderStatus.Cancelled,
                    "3" => OrderStatus.Delivered,
                    _ => OrderStatus.Delivered
                };
            }
        }

        return OrderStatus.Delivered;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        switch (propertyElement.ValueKind)
        {
            case JsonValueKind.String:
                value = propertyElement.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(value);
            case JsonValueKind.Number:
                value = propertyElement.GetRawText();
                return !string.IsNullOrWhiteSpace(value);
            default:
                return false;
        }
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Number => propertyElement.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(propertyElement.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetLong(JsonElement element, string property, out long value)
    {
        value = 0;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        return propertyElement.ValueKind switch
        {
            JsonValueKind.Number => propertyElement.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(propertyElement.GetString(), out value),
            _ => false
        };
    }

    private static bool TryGetObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind == JsonValueKind.Object)
        {
            value = propertyElement;
            return true;
        }

        return false;
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement))
        {
            return false;
        }

        if (propertyElement.ValueKind == JsonValueKind.Array)
        {
            value = propertyElement;
            return true;
        }

        return false;
    }

    private static bool TryGetEmbeddedObject(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetEmbeddedArray(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        if (!element.TryGetProperty(property, out var propertyElement) ||
            propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var raw = propertyElement.GetString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetLatestHistory(JsonElement historyArray, out JsonElement history)
    {
        history = default;

        if (historyArray.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        JsonElement? latest = null;
        long? latestTime = null;
        foreach (var entry in historyArray.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("type_history", out _))
            {
                if (TryGetLong(entry, "time", out var timestamp))
                {
                    if (latestTime is null || timestamp >= latestTime)
                    {
                        latestTime = timestamp;
                        latest = entry;
                    }
                }
                else if (latestTime is null)
                {
                    latest = entry;
                }
            }
        }

        if (latest is not null)
        {
            history = latest.Value;
            return true;
        }

        return false;
    }
}

public sealed record TransactionStatusUpdate(string TransactionId, OrderStatus Status, string TypeHistory);
