using System.Globalization;

namespace HRManagement.TestSupport.Time;

/// <summary>
/// QA-034: a clock that always returns the same instant, so tests over dated documents (STR-005 exceptions,
/// STR-002 legislative watch) do not turn red on a calendar day.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public static FixedTimeProvider AtUtcDate(int year, int month, int day) =>
        new(new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Noon UTC of a <c>yyyy-MM-dd</c> date, parsed with the invariant culture.</summary>
    public static FixedTimeProvider AtUtcDate(string isoDate) =>
        new(new DateTimeOffset(
            DateTime.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(12),
            TimeSpan.Zero));

    public override DateTimeOffset GetUtcNow() => utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
