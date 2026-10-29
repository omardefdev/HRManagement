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

    /// <summary>
    /// COM-030: email for display (notification history) — first character of the local part, then <c>***</c>, domain
    /// kept: <c>ahmad@x.com</c> → <c>a***@x.com</c>. Not an email → masked like a phone.
    /// </summary>
    public static string Email(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Trim();
        var at = v.LastIndexOf('@');
        if (at <= 0 || at == v.Length - 1)
            return Phone(v);
        return v[0] + "***" + v[at..];
    }

    /// <summary>
    /// COM-030: phone for display — the first two and last two characters, stars between:
    /// <c>0999000012</c> → <c>09******12</c>, <c>+963999000012</c> → <c>+9*********12</c>. Four characters or fewer → stars.
    /// </summary>
    public static string Phone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var v = value.Trim();
        if (v.Length <= 4)
            return new string('*', v.Length);
        return v[..2] + new string('*', v.Length - 4) + v[^2..];
    }

    public static string Sha256Hex(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
