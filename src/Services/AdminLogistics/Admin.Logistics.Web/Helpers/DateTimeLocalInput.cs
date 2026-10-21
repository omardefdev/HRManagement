namespace Admin.Logistics.Web.Helpers;

/// <summary>Normalizes <see cref="DateTime"/> for HTML <c>datetime-local</c> inputs (minute precision).</summary>
internal static class DateTimeLocalInput
{
    public static DateTime TruncateToMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Kind);

    public static DateTime? TruncateToMinute(DateTime? value) =>
        value.HasValue ? TruncateToMinute(value.Value) : null;
}
