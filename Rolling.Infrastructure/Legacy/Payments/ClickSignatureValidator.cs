using System.Security.Cryptography;
using System.Text;
using Rolling.Infrastructure.Configuration;

namespace Rolling.Infrastructure.Payments;

public sealed class ClickSignatureValidator
{
    private readonly ClickOptions _options;

    public ClickSignatureValidator(ClickOptions options)
    {
        _options = options;
    }

    public bool Validate(ClickSignaturePayload payload, string providedSignature)
    {
        using var md5 = MD5.Create();
        var signature = $"{payload.ClickTransactionId}{payload.ServiceId}{_options.SecretKey}{payload.OrderId}{payload.MerchantPrepareId}{payload.Amount}{payload.Action}{payload.SignTime}";
        var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(signature)));
        return hash.Equals(providedSignature, StringComparison.OrdinalIgnoreCase);
    }

    public readonly record struct ClickSignaturePayload(
        string ClickTransactionId,
        string ServiceId,
        string OrderId,
        string? MerchantPrepareId,
        decimal Amount,
        int Action,
        long SignTime);
}
