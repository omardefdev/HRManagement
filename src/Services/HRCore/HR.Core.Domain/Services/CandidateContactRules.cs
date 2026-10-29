using System.Net.Mail;
using System.Text;

namespace HR.Core.Domain.Services;

/// <summary>
/// COM-030 (PLAN-30 F2): contact checks of the public apply form. Phones are stored in E.164 form (<c>+963999000000</c>)
/// so the SMS channel can use them as is; emails are trimmed.
/// </summary>
public static class CandidateContactRules
{
    /// <summary>E.164 allows at most 15 digits; below 8 no real subscriber number exists.</summary>
    public const int MinPhoneDigits = 8;
    public const int MaxPhoneDigits = 15;

    /// <summary>Subscriber part (after the country code) of a national number.</summary>
    public const int MinNationalDigits = 7;

    public const int MaxEmailLength = 150;

    /// <summary>
    /// Normalizes a phone to E.164: separators (spaces, <c>-</c>, <c>.</c>, <c>/</c>, parentheses) are removed and
    /// Arabic-Indic digits converted; <c>+…</c> and <c>00…</c> are international; <c>0…</c> is national with a trunk prefix
    /// (<c>0999000000</c> → <c>+963999000000</c>); digits that already start with the country code are kept; any other
    /// digits get the country code. Returns false for anything that is not 8–15 digits.
    /// </summary>
    public static bool TryNormalizePhone(string? raw, string? countryCode, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var cc = new string((countryCode ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var builder = new StringBuilder();
        foreach (var ch in raw.Trim())
        {
            if (ch is ' ' or '-' or '.' or '/' or '(' or ')' or ' ')
                continue;
            if (ch is >= '٠' and <= '٩')
                builder.Append((char)('0' + (ch - '٠')));
            else if (ch is >= '۰' and <= '۹')
                builder.Append((char)('0' + (ch - '۰')));
            else
                builder.Append(ch);
        }

        var s = builder.ToString();
        string digits;
        if (s.StartsWith('+'))
            digits = s[1..];
        else if (s.StartsWith("00", StringComparison.Ordinal))
            digits = s[2..];
        else if (s.StartsWith('0'))
        {
            if (cc.Length == 0)
                return false;
            digits = cc + s[1..];
        }
        else if (cc.Length > 0 && s.StartsWith(cc, StringComparison.Ordinal) && s.Length - cc.Length >= MinNationalDigits)
            digits = s;
        else if (cc.Length > 0)
            digits = cc + s;
        else
            return false;

        if (digits.Length is < MinPhoneDigits or > MaxPhoneDigits || !digits.All(char.IsAsciiDigit) || digits[0] == '0')
            return false;
        if (cc.Length > 0 && digits.StartsWith(cc, StringComparison.Ordinal) && digits.Length - cc.Length < MinNationalDigits)
            return false;

        normalized = "+" + digits;
        return true;
    }

    /// <summary>A single plain address (<c>local@domain.tld</c>), at most <see cref="MaxEmailLength"/> characters.</summary>
    public static bool IsValidEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var v = value.Trim();
        if (v.Length > MaxEmailLength || v.Any(char.IsWhiteSpace) || v.Count(c => c == '@') != 1)
            return false;
        var at = v.IndexOf('@');
        var domain = v[(at + 1)..];
        if (at == 0 || domain.Length < 3 || !domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.')
            || domain.Contains(".."))
            return false;
        return MailAddress.TryCreate(v, out var parsed) && string.Equals(parsed.Address, v, StringComparison.Ordinal);
    }
}
