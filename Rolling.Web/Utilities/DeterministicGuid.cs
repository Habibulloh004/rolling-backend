using System.Security.Cryptography;
using System.Text;

namespace Rolling.Web.Utilities;

internal static class DeterministicGuid
{
    public static Guid From(string value)
    {
        var trimmed = value?.Trim();
        if (Guid.TryParse(trimmed, out var parsed))
        {
            return parsed;
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return Guid.NewGuid();
        }

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(trimmed));
        return new Guid(hash, bigEndian: true);
    }

    public static Guid FromComponents(params string[] components) =>
        From(string.Join("|", components));
}
