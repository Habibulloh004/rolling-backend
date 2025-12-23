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
