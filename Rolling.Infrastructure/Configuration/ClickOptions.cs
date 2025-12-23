namespace Rolling.Infrastructure.Configuration;

public sealed class ClickOptions
{
    public const string SectionName = "Click";

    public string MerchantId { get; init; } = string.Empty;

    public string ServiceId { get; init; } = string.Empty;

    public string MerchantUserId { get; init; } = string.Empty;

    public string SecretKey { get; init; } = string.Empty;

    public string CheckoutBaseUrl { get; init; } = "https://my.click.uz/services/pay";
}
