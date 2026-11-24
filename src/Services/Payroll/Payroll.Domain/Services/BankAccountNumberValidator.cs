using System.Text;
using System.Text.RegularExpressions;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Services;

/// <summary>Why an account number was refused. The caller turns it into its Arabic message; no answer ever carries the number.</summary>
public enum BankAccountNumberError
{
    None = 0,
    Missing = 1,
    IbanForm = 2,
    IbanCheckDigits = 3,
    BankRequired = 4,
    UnknownBank = 5,
    LocalForm = 6,
    TooLong = 7,
}

/// <summary>The answer of <see cref="BankAccountNumberValidator.Validate"/>: the normalised number when it passed.</summary>
public readonly record struct BankAccountNumberCheck(BankAccountNumberError Error, string Normalized)
{
    public bool IsValid => Error == BankAccountNumberError.None;
}

/// <summary>
/// PLAN-55 (D-PAY-11): the identifier of an employee's bank account. An IBAN is always accepted after the ISO 13616 check: its form
/// (two letters, two digits, then letters and digits) and the mod 97 remainder of 1. A local number is accepted only for a bank that
/// <c>Payroll:Bank:Banks</c> defines, by that bank's pattern.
/// <para>
/// <b>No length per country is assumed.</b> Syria is outside the IBAN registry, so a table of national lengths would refuse or accept by a
/// rule nobody issued. The only lengths checked are the standard's own: at most 34 characters, and at least 15 (the shortest IBAN any
/// country registered).
/// </para>
/// </summary>
public static class BankAccountNumberValidator
{
    public const int IbanMinLength = 15;
    public const int IbanMaxLength = 34;

    /// <summary>The longest a bank's pattern may take on one number (a pattern is configuration, but a number is input).</summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>The number as it is stored and compared: no white space, upper case.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (!char.IsWhiteSpace(c))
                builder.Append(char.ToUpperInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Checks a number of the given kind. <paramref name="bankPattern"/> is the pattern of the bank the caller resolved from its code:
    /// null when the code names no defined bank (<paramref name="bankCodeGiven"/> says whether a code was given at all).
    /// </summary>
    public static BankAccountNumberCheck Validate(BankAccountKind kind, string? raw, bool bankCodeGiven, string? bankPattern)
    {
        var number = Normalize(raw);
        if (number.Length == 0)
            return new BankAccountNumberCheck(BankAccountNumberError.Missing, number);

        if (kind == BankAccountKind.Iban)
        {
            // A bank code is optional for an IBAN; one that is given must name a defined bank.
            if (bankCodeGiven && bankPattern is null)
                return new BankAccountNumberCheck(BankAccountNumberError.UnknownBank, number);
            return new BankAccountNumberCheck(CheckIban(number), number);
        }

        if (!bankCodeGiven)
            return new BankAccountNumberCheck(BankAccountNumberError.BankRequired, number);
        if (bankPattern is null)
            return new BankAccountNumberCheck(BankAccountNumberError.UnknownBank, number);
        if (number.Length > IbanMaxLength)
            return new BankAccountNumberCheck(BankAccountNumberError.TooLong, number);
        return new BankAccountNumberCheck(
            MatchesWhole(number, bankPattern) ? BankAccountNumberError.None : BankAccountNumberError.LocalForm, number);
    }

    /// <summary>ISO 13616: the form, then ISO 7064 mod 97-10 (the remainder is 1).</summary>
    public static BankAccountNumberError CheckIban(string normalized)
    {
        if (normalized.Length is < IbanMinLength or > IbanMaxLength)
            return BankAccountNumberError.IbanForm;
        if (!IsAsciiLetter(normalized[0]) || !IsAsciiLetter(normalized[1]) || !IsAsciiDigit(normalized[2]) || !IsAsciiDigit(normalized[3]))
            return BankAccountNumberError.IbanForm;
        for (var i = 4; i < normalized.Length; i++)
        {
            if (!IsAsciiLetter(normalized[i]) && !IsAsciiDigit(normalized[i]))
                return BankAccountNumberError.IbanForm;
        }

        // The check digits a correct IBAN carries are 02 to 98: 00, 01 and 99 give the same remainder as a valid pair and are not issued.
        var check = ((normalized[2] - '0') * 10) + (normalized[3] - '0');
        if (check is < 2 or > 98)
            return BankAccountNumberError.IbanCheckDigits;

        return Mod97(normalized) == 1 ? BankAccountNumberError.None : BankAccountNumberError.IbanCheckDigits;
    }

    /// <summary>The two check digits of an IBAN made of this country code and BBAN (for the tests' own numbers; never a real account).</summary>
    public static string ComputeIbanCheckDigits(string countryCode, string bban)
    {
        var remainder = Mod97(Normalize(countryCode) + "00" + Normalize(bban));
        return (98 - remainder).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The remainder by 97 of the IBAN's digits: the first four characters moved to the end, a letter worth 10 to 35.</summary>
    private static int Mod97(string iban)
    {
        var remainder = 0;
        for (var i = 0; i < iban.Length; i++)
        {
            var c = iban[(i + 4) % iban.Length];
            if (IsAsciiDigit(c))
            {
                remainder = ((remainder * 10) + (c - '0')) % 97;
            }
            else
            {
                var value = c - 'A' + 10;
                remainder = ((remainder * 100) + value) % 97;
            }
        }

        return remainder;
    }

    private static bool MatchesWhole(string number, string pattern)
    {
        try
        {
            var match = Regex.Match(number, pattern, RegexOptions.CultureInvariant, PatternTimeout);
            return match.Success && match.Index == 0 && match.Length == number.Length;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            // A pattern that does not compile accepts nothing; the options' start-up check names it.
            return false;
        }
    }

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z';

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';
}
