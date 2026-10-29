using System.Globalization;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// QA-034: dates in the governance documents (STR-005 exceptions, STR-002 legislative watch) are <c>yyyy-MM-dd</c>
/// calendar days in UTC. Parsed with the invariant culture so a Hijri thread culture (ar-SA) cannot shift the year.
/// </summary>
internal static class GovernanceDateParser
{
    public static bool TryParseUtcDate(string? value, out DateTime utcDate)
    {
        if (DateTime.TryParseExact(
                value?.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utcDate = parsed.Date;
            return true;
        }

        utcDate = default;
        return false;
    }
}
