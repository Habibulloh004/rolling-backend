using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rolling.Web.Models.Payments;

public sealed class ClickPrepareRequestDto
{
    [JsonPropertyName("click_trans_id")]
    public string ClickTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("service_id")]
    public string ServiceId { get; set; } = string.Empty;

    [JsonPropertyName("merchant_trans_id")]
    public string MerchantTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("action")]
    public int Action { get; set; }

    [JsonPropertyName("sign_time")]
    public long SignTime { get; set; }

    [JsonIgnore]
    public string? SignTimeRaw { get; set; }

    [JsonIgnore]
    public string? AmountRaw { get; set; }

    [JsonPropertyName("sign_string")]
    public string SignString { get; set; } = string.Empty;
}

public sealed class ClickCompleteRequestDto
{
    [JsonPropertyName("click_trans_id")]
    public string ClickTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("service_id")]
    public string ServiceId { get; set; } = string.Empty;

    [JsonPropertyName("merchant_trans_id")]
    public string MerchantTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("merchant_prepare_id")]
    public string? MerchantPrepareId { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("action")]
    public int Action { get; set; }

    [JsonPropertyName("sign_time")]
    public long SignTime { get; set; }

    [JsonIgnore]
    public string? SignTimeRaw { get; set; }

    [JsonIgnore]
    public string? AmountRaw { get; set; }

    [JsonPropertyName("sign_string")]
    public string SignString { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public int ErrorCode { get; set; }
}

public sealed class ClickCheckoutRequestDto
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

public sealed class ClickFakeTransactionRequestDto
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

    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; set; }

    [JsonPropertyName("prepare_id")]
    public string? PrepareId { get; set; }
}
