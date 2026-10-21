using System.Text.RegularExpressions;

namespace HRManagement.ServiceDefaults.Integration;

public static partial class HrLookupDisplayHelper
{
    private static readonly HashSet<string> UnresolvedPlaceholders = new(StringComparer.Ordinal)
    {
        "غير متاح",
        "جاري التحميل...",
    };

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase)]
    private static partial Regex GuidPattern();

    public static bool IsGuidDisplay(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GuidPattern().IsMatch(value.Trim());

    public static bool IsResolvedEmployeeName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !IsGuidDisplay(value)
        && !UnresolvedPlaceholders.Contains(value.Trim());
}
