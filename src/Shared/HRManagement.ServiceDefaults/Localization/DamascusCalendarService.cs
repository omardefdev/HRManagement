using System.Globalization;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>Gregorian formatting in Asia/Damascus; optional Hijri via System.Globalization.</summary>
public sealed class DamascusCalendarService : ICalendarService
{
    private static readonly TimeZoneInfo DamascusZone = ResolveDamascusZone();
    private static readonly HijriCalendar Hijri = new();

    public TimeZoneInfo TimeZone => DamascusZone;

    public DateTimeOffset ToLocalTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, DamascusZone);

    public string FormatGregorianDate(DateTime date, string? cultureName = null)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName ?? HrLocalizationExtensions.DefaultCultureName);
        return date.ToString("d", culture);
    }

    public string? FormatHijriDate(DateTime date)
    {
        try
        {
            int y = Hijri.GetYear(date);
            int m = Hijri.GetMonth(date);
            int d = Hijri.GetDayOfMonth(date);
            return $"{y:D4}/{m:D2}/{d:D2}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static TimeZoneInfo ResolveDamascusZone()
    {
        var id = OperatingSystem.IsWindows() ? "Syria Standard Time" : "Asia/Damascus";
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }
}
