using System.Security.Cryptography;
using System.Text;

namespace HRManagement.SharedKernel;

/// <summary>GOV-001: minimize PII in exports and discovery packages.</summary>
public static class PiiMask
{
    /// <summary>Mask national ID for general exports — keeps last 4 digits when long enough.</summary>
    public static string NationalId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Trim();
        if (v.Length <= 4)
            return new string('*', v.Length);
        return new string('*', Math.Min(8, v.Length - 4)) + v[^4..];
    }

    public static string Sha256Hex(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
