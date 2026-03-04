using System.Globalization;
using System.Text.Json;
using Rolling.Infrastructure.Persistence.Postgres.Entities;
using Rolling.Infrastructure.Poster;

namespace Rolling.Web.Services;

public sealed class PosterClientCommentLengthService
{
    private readonly PosterService _posterService;
    private readonly ILogger<PosterClientCommentLengthService> _logger;

    public PosterClientCommentLengthService(
        PosterService posterService,
        ILogger<PosterClientCommentLengthService> logger)
    {
        _posterService = posterService;
        _logger = logger;
    }

    public async Task TryIncrementLengthAsync(Order order, string source, CancellationToken cancellationToken)
    {
        try
        {
            var clientId = order.UserId?.Trim();
            if (string.IsNullOrWhiteSpace(clientId))
            {
                _logger.LogDebug(
                    "Poster client length update skipped - OrderId={OrderId}, Reason=empty_user_id, Source={Source}",
                    order.Id,
                    source);
                return;
            }

            using var clientDocument = await _posterService.GetClientAsync(clientId, cancellationToken);
            if (clientDocument is null)
            {
                _logger.LogWarning(
                    "Poster client length update skipped - client lookup failed: OrderId={OrderId}, ClientId={ClientId}, Source={Source}",
                    order.Id,
                    clientId,
                    source);
                return;
            }

            if (!TryExtractClient(clientDocument.RootElement, out var client))
            {
                _logger.LogWarning(
                    "Poster client length update skipped - invalid client response: OrderId={OrderId}, ClientId={ClientId}, Source={Source}",
                    order.Id,
                    clientId,
                    source);
                return;
            }

            var oldComment = GetPropertyValue(client, "comment") ?? string.Empty;
            var oldMetadata = ParseCommentMetadata(oldComment);
            var currentPosterOrderKey = BuildPosterOrderKey(order);

            if (string.Equals(oldMetadata.LastOrderId, order.Id, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(currentPosterOrderKey) &&
                 string.Equals(oldMetadata.LastPosterOrderKey, currentPosterOrderKey, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogInformation(
                    "Poster client comment length increment skipped - already processed: OrderId={OrderId}, ClientId={ClientId}, Source={Source}, PosterOrderKey={PosterOrderKey}",
                    order.Id,
                    clientId,
                    source,
                    currentPosterOrderKey ?? "<none>");
                return;
            }

            var newLength = oldMetadata.Length == int.MaxValue ? int.MaxValue : oldMetadata.Length + 1;
            var newMetadata = oldMetadata with
            {
                Length = newLength,
                LastOrderId = order.Id,
                LastPosterOrderKey = currentPosterOrderKey
            };
            var newComment = BuildCommentJson(newMetadata);

            using var updateDocument = await _posterService.UpdateClientCommentAsync(clientId, newComment, cancellationToken);
            if (updateDocument is null)
            {
                _logger.LogWarning(
                    "Poster client length update failed - updateClient returned null: OrderId={OrderId}, ClientId={ClientId}, Source={Source}",
                    order.Id,
                    clientId,
                    source);
                return;
            }

            _logger.LogInformation(
                "Poster client comment length incremented: OrderId={OrderId}, ClientId={ClientId}, OldLength={OldLength}, NewLength={NewLength}, Source={Source}",
                order.Id,
                clientId,
                oldMetadata.Length,
                newLength,
                source);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while incrementing Poster client comment length: OrderId={OrderId}, Source={Source}",
                order.Id,
                source);
        }
    }

    private static bool TryExtractClient(JsonElement root, out JsonElement client)
    {
        client = default;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("response", out var response))
        {
            if (response.ValueKind == JsonValueKind.Object)
            {
                client = response;
                return true;
            }

            if (response.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in response.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        client = item;
                        return true;
                    }
                }
            }
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("client_id", out _))
        {
            client = root;
            return true;
        }

        return false;
    }

    private static string? GetPropertyValue(JsonElement source, string key)
    {
        if (!source.TryGetProperty(key, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static CommentMetadata ParseCommentMetadata(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return CommentMetadata.Empty;
        }

        var trimmed = comment.Trim();
        if (TryParseMetadataJson(trimmed, out var metadata))
        {
            return metadata;
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            try
            {
                var unescaped = JsonSerializer.Deserialize<string>(trimmed);
                if (TryParseMetadataJson(unescaped, out metadata))
                {
                    return metadata;
                }
            }
            catch (JsonException)
            {
                // Ignore and continue fallback parsing.
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            var objectPayload = trimmed[start..(end + 1)];
            if (TryParseMetadataJson(objectPayload, out metadata))
            {
                return metadata;
            }
        }

        return CommentMetadata.Empty;
    }

    private static bool TryParseMetadataJson(string? json, out CommentMetadata metadata)
    {
        metadata = CommentMetadata.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var root = document.RootElement;
            var password = GetPropertyValue(root, "password");
            var length = ParseLength(root);
            var lastOrderId = GetPropertyValue(root, "last_order_id");
            var lastPosterOrderKey = GetPropertyValue(root, "last_poster_order_key");
            metadata = new CommentMetadata(password, length, lastOrderId, lastPosterOrderKey);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static int ParseLength(JsonElement metadata)
    {
        if (!metadata.TryGetProperty("length", out var lengthProperty))
        {
            return 0;
        }

        var parsed = lengthProperty.ValueKind switch
        {
            JsonValueKind.Number when lengthProperty.TryGetInt32(out var numberValue) => numberValue,
            JsonValueKind.String when int.TryParse(
                lengthProperty.GetString()?.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var stringValue) => stringValue,
            _ => 0
        };

        return parsed < 0 ? 0 : parsed;
    }

    private static string BuildCommentJson(CommentMetadata metadata)
    {
        var payload = new Dictionary<string, string>
        {
            ["length"] = metadata.Length.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(metadata.Password))
        {
            payload["password"] = metadata.Password.Trim();
        }

        if (!string.IsNullOrWhiteSpace(metadata.LastOrderId))
        {
            payload["last_order_id"] = metadata.LastOrderId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(metadata.LastPosterOrderKey))
        {
            payload["last_poster_order_key"] = metadata.LastPosterOrderKey.Trim();
        }

        return JsonSerializer.Serialize(payload);
    }

    private static string? BuildPosterOrderKey(Order order)
    {
        var transactionId = NormalizeOrderIdentifier(order.PosterTransactionId);
        if (!string.IsNullOrWhiteSpace(transactionId))
        {
            return $"tx:{transactionId}";
        }

        var incomingOrderId = NormalizeOrderIdentifier(order.PosterIncomingOrderId);
        if (!string.IsNullOrWhiteSpace(incomingOrderId))
        {
            return $"in:{incomingOrderId}";
        }

        return null;
    }

    private static string? NormalizeOrderIdentifier(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed record CommentMetadata(
        string? Password,
        int Length,
        string? LastOrderId,
        string? LastPosterOrderKey)
    {
        public static CommentMetadata Empty { get; } = new(null, 0, null, null);
    }
}
