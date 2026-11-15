using HRManagement.ServiceDefaults.Localization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.Web.Shared.Html;

/// <summary>
/// PLAN-67 (D-67-2, G-UX-05): every date and time a view shows or puts in an input goes through these helpers, and the interface
/// guard finds any other formatting. Dates read <c>yyyy/MM/dd</c>; times read <c>yyyy/MM/dd HH:mm</c> in the configured zone
/// (<c>Localization:TimeZone</c>, through <see cref="ICalendarService"/>) inside a <c>time</c> element whose title says
/// «بتوقيت دمشق» and whose <c>datetime</c> keeps the stored UTC value. A missing value shows <c>—</c> unless the view passes another.
/// A <see cref="DateOnly"/> is formatted, never converted.
/// </summary>
public static class HrTimeHtmlExtensions
{
    public const string Missing = "—";

    private static TimeZoneInfo Zone(IHtmlHelper html) =>
        html.ViewContext?.HttpContext?.RequestServices?.GetService<ICalendarService>()?.TimeZone ?? HrLocalTime.DefaultZone;

    private static IHtmlContent Text(string text) => new HtmlString(System.Net.WebUtility.HtmlEncode(text));

    // ------------------------------------------------------------------ dates

    public static IHtmlContent HrDate(this IHtmlHelper html, DateOnly? value, string missing = Missing) =>
        Text(value is { } d ? HrLocalTime.FormatDate(d) : missing);

    public static IHtmlContent HrDate(this IHtmlHelper html, DateTime? value, string missing = Missing) =>
        Text(value is { } d ? HrLocalTime.FormatDate(d, Zone(html)) : missing);

    public static IHtmlContent HrDate(this IHtmlHelper html, DateTimeOffset? value, string missing = Missing) =>
        Text(value is { } d ? HrLocalTime.FormatDate(d, Zone(html)) : missing);

    // ------------------------------------------------------------------ times

    public static IHtmlContent HrDateTime(this IHtmlHelper html, DateTime? value, string missing = Missing) =>
        value is { } d ? Time(HrLocalTime.Iso(d), HrLocalTime.FormatDateTime(d, Zone(html))) : Text(missing);

    public static IHtmlContent HrDateTime(this IHtmlHelper html, DateTimeOffset? value, string missing = Missing) =>
        value is { } d ? Time(HrLocalTime.Iso(d.UtcDateTime), HrLocalTime.FormatDateTime(d, Zone(html))) : Text(missing);

    private static IHtmlContent Time(string iso, string shown) =>
        new HtmlString($"<time datetime=\"{iso}\" title=\"{HrLocalTime.ZoneHintAr}\" dir=\"ltr\">{System.Net.WebUtility.HtmlEncode(shown)}</time>");

    /// <summary>A time of day (a shift's start, an office hour): a wall-clock time, formatted <c>HH:mm</c> and never converted.</summary>
    public static IHtmlContent HrTime(this IHtmlHelper html, TimeOnly? value, string missing = Missing) =>
        Text(value is { } t ? t.ToString(HrLocalTime.TimeOfDayFormat, System.Globalization.CultureInfo.InvariantCulture) : missing);

    public static IHtmlContent HrTime(this IHtmlHelper html, TimeSpan? value, string missing = Missing) =>
        Text(value is { } t ? t.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture) : missing);

    // ------------------------------------------------------------------ text (code blocks, attributes, joined strings)

    public static string HrDateText(this IHtmlHelper html, DateOnly? value, string missing = Missing) =>
        value is { } d ? HrLocalTime.FormatDate(d) : missing;

    public static string HrDateText(this IHtmlHelper html, DateTime? value, string missing = Missing) =>
        value is { } d ? HrLocalTime.FormatDate(d, Zone(html)) : missing;

    public static string HrDateText(this IHtmlHelper html, DateTimeOffset? value, string missing = Missing) =>
        value is { } d ? HrLocalTime.FormatDate(d, Zone(html)) : missing;

    public static string HrDateTimeText(this IHtmlHelper html, DateTime? value, string missing = Missing) =>
        value is { } d ? HrLocalTime.FormatDateTime(d, Zone(html)) : missing;

    public static string HrDateTimeText(this IHtmlHelper html, DateTimeOffset? value, string missing = Missing) =>
        value is { } d ? HrLocalTime.FormatDateTime(d, Zone(html)) : missing;

    // ------------------------------------------------------------------ input values

    /// <summary>The value of <c>&lt;input type="date"&gt;</c>.</summary>
    public static string HrDateInputValue(this IHtmlHelper html, DateOnly? value) =>
        value?.ToString(HrLocalTime.DateInputFormat, System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>The value of <c>&lt;input type="date"&gt;</c> for a stored time: its date on the user's clock.</summary>
    public static string HrDateInputValue(this IHtmlHelper html, DateTime? value) =>
        value is { } d ? HrLocalTime.ToLocal(d, Zone(html)).ToString(HrLocalTime.DateInputFormat, System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>The value of <c>&lt;input type="datetime-local"&gt;</c>: the stored time on the user's clock (read back by the model binder).</summary>
    public static string HrDateTimeLocalValue(this IHtmlHelper html, DateTime? value) =>
        value is { } d ? HrLocalTime.ToLocal(d, Zone(html)).ToString(HrLocalTime.DateTimeLocalInputFormat, System.Globalization.CultureInfo.InvariantCulture) : "";

    public static string HrDateTimeLocalValue(this IHtmlHelper html, DateTimeOffset? value) =>
        value is { } d ? HrLocalTime.ToLocal(d, Zone(html)).ToString(HrLocalTime.DateTimeLocalInputFormat, System.Globalization.CultureInfo.InvariantCulture) : "";

    /// <summary>The value of <c>&lt;input type="month"&gt;</c>.</summary>
    public static string HrMonthInputValue(this IHtmlHelper html, DateOnly? value) =>
        value?.ToString(HrLocalTime.MonthInputFormat, System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public static string HrMonthInputValue(this IHtmlHelper html, DateTime? value) =>
        value is { } d ? HrLocalTime.ToLocal(d, Zone(html)).ToString(HrLocalTime.MonthInputFormat, System.Globalization.CultureInfo.InvariantCulture) : "";
}
