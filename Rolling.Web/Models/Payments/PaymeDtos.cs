using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Payments;

public sealed class PaymeRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }
}

public sealed class PaymeCheckoutRequestDto
{
    [JsonPropertyName("orderDetails")]
    public JsonElement? OrderDetails { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}

public sealed class PaymeFakeTransactionRequestDto
{
    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("orderDetails")]
    public JsonElement? OrderDetails { get; set; }

    [JsonPropertyName("status")]
    public int? Status { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    [JsonPropertyName("order_id")]
    public string? OrderId { get; set; }

    [JsonPropertyName("create_time")]
    public long? CreateTime { get; set; }

    [JsonPropertyName("perform_time")]
    public long? PerformTime { get; set; }

    [JsonPropertyName("cancel_time")]
    public long? CancelTime { get; set; }

    [JsonPropertyName("reason")]
    public int? Reason { get; set; }

    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("prepare_id")]
    public string? PrepareId { get; set; }
}
