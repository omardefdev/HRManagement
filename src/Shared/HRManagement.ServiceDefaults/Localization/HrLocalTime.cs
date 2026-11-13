using System.Globalization;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>
/// PLAN-67 (D-67-2, G-UX-05): the one place that turns a stored time into the time a user reads, and back. Storage stays UTC; the
/// interface shows and takes times in the configured zone (<c>Localization:TimeZone</c>, <c>Asia/Damascus</c> by default), with one
/// date format (<see cref="DateFormat"/>) and one time format (<see cref="DateTimeFormat"/>). A <see cref="DateOnly"/> is a calendar
/// date: it is formatted, never converted.
/// </summary>
public static class HrLocalTime
{
    public const string TimeZoneKey = "Localization:TimeZone";
    public const string DefaultTimeZoneId = "Asia/Damascus";

    /// <summary>D-67-2: dates.</summary>
    public const string DateFormat = "yyyy/MM/dd";

    /// <summary>D-67-2: times.</summary>
    public const string DateTimeFormat = "yyyy/MM/dd HH:mm";

    /// <summary>A time of day (<see cref="TimeOnly"/>), never converted.</summary>
    public const string TimeOfDayFormat = "HH:mm";

    /// <summary>The value of an HTML date input.</summary>
    public const string DateInputFormat = "yyyy-MM-dd";

    /// <summary>The value of an HTML datetime-local input: a local time, no offset.</summary>
    public const string DateTimeLocalInputFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>The value of an HTML month input.</summary>
    public const string MonthInputFormat = "yyyy-MM";

    /// <summary>The hint every displayed time carries (D-67-2).</summary>
    public const string ZoneHintAr = "بتوقيت دمشق";

    private static readonly string[] LocalInputFormats =
    [
        "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ss.f", "yyyy-MM-ddTHH:mm:ss.ff", "yyyy-MM-ddTHH:mm:ss.fff",
        "yyyy-MM-ddTHH:mm:ss.ffff", "yyyy-MM-ddTHH:mm:ss.fffff", "yyyy-MM-ddTHH:mm:ss.ffffff", "yyyy-MM-ddTHH:mm:ss.fffffff"
    ];

    /// <summary>Resolves a zone id; IANA ids work on Windows through ICU, and the Windows id is the fallback for Damascus.</summary>
    public static TimeZoneInfo ResolveZone(string? id)
    {
        var wanted = string.IsNullOrWhiteSpace(id) ? DefaultTimeZoneId : id.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(wanted);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            if (string.Equals(wanted, DefaultTimeZoneId, StringComparison.OrdinalIgnoreCase) && OperatingSystem.IsWindows())
                return TimeZoneInfo.FindSystemTimeZoneById("Syria Standard Time");
            throw new InvalidOperationException($"{TimeZoneKey} names an unknown time zone: '{wanted}'.", ex);
        }
    }

    /// <summary>The default zone (Asia/Damascus).</summary>
    public static TimeZoneInfo DefaultZone { get; } = ResolveZone(DefaultTimeZoneId);

    /// <summary>
    /// The configured zone, for code that formats a time outside a view (the candidate portal and its messages, generated documents):
    /// <see cref="DamascusCalendarService"/> sets it from <c>Localization:TimeZone</c> when the service starts; the default until then.
    /// </summary>
    public static TimeZoneInfo Zone { get; private set; } = DefaultZone;

    public static void UseZone(TimeZoneInfo zone) => Zone = zone;

    /// <summary>
    /// A stored time as the user's clock shows it. A <see cref="DateTimeKind.Unspecified"/> value is a stored UTC value (EF and SQLite
    /// return them so); a <see cref="DateTimeKind.Local"/> value is first made universal.
    /// </summary>
    public static DateTime ToLocal(DateTime stored, TimeZoneInfo zone)
    {
        var utc = stored.Kind switch
        {
            DateTimeKind.Utc => stored,
            DateTimeKind.Local => stored.ToUniversalTime(),
            _ => DateTime.SpecifyKind(stored, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
    }

    public static DateTime ToLocal(DateTimeOffset stored, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(stored.UtcDateTime, zone);

    /// <summary>A time the user entered on his clock (a datetime-local value), as the UTC value to store.</summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        if (local.Kind == DateTimeKind.Utc)
            return local;
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    /// <summary>
    /// A datetime-local value: <c>yyyy-MM-ddTHH:mm</c> with optional seconds and fractions, and no offset or <c>Z</c>. Anything else
    /// (a date alone, an ISO value with an offset) is not one.
    /// </summary>
    public static bool TryParseLocalInput(string? text, out DateTime local)
    {
        local = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return DateTime.TryParseExact(text.Trim(), LocalInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out local);
    }

    /// <summary>
    /// A time a user typed in a form: a datetime-local value (no offset) is on the user's clock and is converted to UTC; an ISO value
    /// with <c>Z</c> or an offset says what it means and is read as such. False for anything else.
    /// </summary>
    public static bool TryParseUserTime(string? text, TimeZoneInfo zone, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (TryParseLocalInput(text, out var local))
        {
            utc = ToUtc(local, zone);
            return true;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return true;
    }

    public static string FormatDate(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string FormatDate(DateTime stored, TimeZoneInfo zone) =>
        ToLocal(stored, zone).ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string FormatDate(DateTimeOffset stored, TimeZoneInfo zone) =>
        ToLocal(stored, zone).ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTime stored, TimeZoneInfo zone) =>
        ToLocal(stored, zone).ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTimeOffset stored, TimeZoneInfo zone) =>
        ToLocal(stored, zone).ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>The ISO value of a stored time in UTC, for a <c>time</c> element's <c>datetime</c> attribute.</summary>
    public static string Iso(DateTime stored)
    {
        var utc = stored.Kind == DateTimeKind.Local ? stored.ToUniversalTime() : DateTime.SpecifyKind(stored, DateTimeKind.Utc);
        return utc.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
