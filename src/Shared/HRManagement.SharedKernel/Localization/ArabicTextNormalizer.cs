using System.Text;
using System.Text.RegularExpressions;

namespace HRManagement.SharedKernel.Localization;

/// <summary>
/// L10N-002: Arabic text normalizer for search, deduplication, and unique constraints.
/// Normalizes common Arabic orthographic variants without modifying the original stored text.
/// The normalized form is used for indexing, matching, and collation.
/// </summary>
public static partial class ArabicTextNormalizer
{
    /// <summary>Versioned rule set — increment when normalization logic changes (triggers re-index).</summary>
    public const int RuleVersion = 1;

    /// <summary>
    /// Normalizes Arabic text by applying canonical rules:
    /// 1. Remove diacritics (tashkeel)
    /// 2. Normalize hamza/alef variants → bare alef
    /// 3. Normalize alef maqsura (ى) → ya (ي)
    /// 4. Normalize ta marbuta (ة) → ha (ه)
    /// 5. Remove tatweel (kashida)
    /// 6. Normalize Arabic-Indic digits (٠-٩) → ASCII digits (0-9)
    /// 7. Collapse whitespace
    /// 8. Trim
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);

        foreach (var ch in input)
        {
            if (IsDiacritic(ch))
                continue;

            if (ch == '\u0640') // tatweel / kashida
                continue;

            var normalized = ch switch
            {
                '\u0622' or '\u0623' or '\u0625' or '\u0671' => '\u0627', // آ أ إ ٱ → ا
                '\u0649' => '\u064A', // ى → ي
                '\u0629' => '\u0647', // ة → ه
                >= '\u0660' and <= '\u0669' => (char)(ch - '\u0660' + '0'), // ٠-٩ → 0-9
                >= '\u06F0' and <= '\u06F9' => (char)(ch - '\u06F0' + '0'), // ۰-۹ → 0-9
                _ => ch
            };

            sb.Append(normalized);
        }

        return CollapseWhitespace(sb.ToString()).Trim();
    }

    /// <summary>
    /// Checks whether two Arabic strings are equivalent under normalization.
    /// </summary>
    public static bool AreEquivalent(string? a, string? b)
        => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static bool IsDiacritic(char ch)
        => ch is >= '\u064B' and <= '\u065F'  // fathatan through hamza below
           or '\u0670'                         // superscript alef
           or >= '\u06D6' and <= '\u06DC'      // Quranic annotations
           or >= '\u06DF' and <= '\u06E4'
           or >= '\u06E7' and <= '\u06E8'
           or >= '\u06EA' and <= '\u06ED';

    private static string CollapseWhitespace(string s)
        => WhitespaceRegex().Replace(s, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
