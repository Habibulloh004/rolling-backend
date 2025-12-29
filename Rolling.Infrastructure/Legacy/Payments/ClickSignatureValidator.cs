using System.Security.Cryptography;
using System.Text;
using System.Globalization;
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
        var amount = NormalizeAmount(payload.AmountForSignature);
        using var md5 = MD5.Create();
        var signature = $"{payload.ClickTransactionId}{payload.ServiceId}{_options.SecretKey}{payload.OrderId}{payload.MerchantPrepareId}{amount}{payload.Action}{payload.SignTimeForSignature}";
        var hash = Convert.ToHexString(md5.ComputeHash(Encoding.UTF8.GetBytes(signature)));
        return hash.Equals(providedSignature, StringComparison.OrdinalIgnoreCase);
    }

    public readonly record struct ClickSignaturePayload(
        string ClickTransactionId,
        string ServiceId,
        string OrderId,
        string? MerchantPrepareId,
        string AmountForSignature,
        int Action,
        string SignTimeForSignature);

    private static string NormalizeAmount(string amount)
    {
        if (decimal.TryParse(amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed.ToString("0.##", CultureInfo.InvariantCulture);
        }

        return amount;
    }
}
