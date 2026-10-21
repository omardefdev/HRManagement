using System.Text.RegularExpressions;

namespace HRManagement.SharedKernel;

/// <summary>PRV-001 / AC-26: national-id lookup via SHA-256 hash (never plaintext column search).</summary>
public static partial class NationalIdSearch
{
    [GeneratedRegex(@"^\d{9,11}$")]
    private static partial Regex DigitNationalIdPattern();

    public static bool LooksLikeNationalId(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
            return false;

        var trimmed = term.Trim();
        if (DigitNationalIdPattern().IsMatch(trimmed))
            return true;

        // Acceptance / alphanumeric national identifiers (e.g. ACC010000001).
        return trimmed.Length is >= 9 and <= 14
               && trimmed.All(c => char.IsLetterOrDigit(c));
    }

    public static string ComputeHash(string term) => PiiMask.Sha256Hex(term.Trim());
}
