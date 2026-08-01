namespace HRManagement.SharedKernel;

public static class DateTimeExtensions
{
    public static DateTime AsUtc(this DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime? AsUtc(this DateTime? value) =>
        value.HasValue ? value.Value.AsUtc() : null;
}
