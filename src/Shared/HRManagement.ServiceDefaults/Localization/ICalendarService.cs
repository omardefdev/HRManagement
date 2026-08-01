namespace HRManagement.ServiceDefaults.Localization;

/// <summary>L10N-001: calendar and timezone helpers for Syria (Asia/Damascus).</summary>
public interface ICalendarService
{
    TimeZoneInfo TimeZone { get; }

    DateTimeOffset ToLocalTime(DateTimeOffset utc);

    string FormatGregorianDate(DateTime date, string? cultureName = null);

    /// <summary>Hijri (Islamic) date using System.Globalization.HijriCalendar; null if out of range.</summary>
    string? FormatHijriDate(DateTime date);
}
