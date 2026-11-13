using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace HRManagement.ServiceDefaults.Localization;

/// <summary>
/// Gregorian formatting in the configured zone; optional Hijri via System.Globalization. PLAN-67 (D-67-2): the zone is
/// <c>Localization:TimeZone</c> (<see cref="HrLocalTime.DefaultTimeZoneId"/>, Asia/Damascus, when absent); an unknown id stops the service.
/// </summary>
public sealed class DamascusCalendarService(IConfiguration? configuration = null) : ICalendarService
{
    private static readonly HijriCalendar Hijri = new();
    private readonly TimeZoneInfo _zone = Configure(HrLocalTime.ResolveZone(configuration?[HrLocalTime.TimeZoneKey]));

    private static TimeZoneInfo Configure(TimeZoneInfo zone)
    {
        HrLocalTime.UseZone(zone);
        return zone;
    }

    public TimeZoneInfo TimeZone => _zone;

    public DateTimeOffset ToLocalTime(DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, _zone);

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
}
