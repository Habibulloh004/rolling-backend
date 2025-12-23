namespace Rolling.Infrastructure.Configuration;

public sealed class PaymeOptions
{
    public const string SectionName = "Payme";

    public string MerchantId { get; init; } = string.Empty;

    public string MerchantKey { get; init; } = string.Empty;

    public string CheckoutBaseUrl { get; init; } = "https://checkout.payme.uz";
}
