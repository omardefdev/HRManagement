using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

/// <summary>
/// Working-day, holiday, and proration helpers driven by active PayrollConfiguration.
/// </summary>
public static class PayrollPeriodRules
{
    public static DayOfWeek[] ParseNonWorkingDays(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [DayOfWeek.Friday, DayOfWeek.Saturday];

        var days = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.TryParse<DayOfWeek>(s, ignoreCase: true, out var d) ? d : (DayOfWeek?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .Distinct()
            .ToArray();

        return days.Length > 0 ? days : [DayOfWeek.Friday, DayOfWeek.Saturday];
    }

    public static bool IsWorkingDay(
        DateOnly date,
        PayrollConfiguration? config = null,
        IReadOnlySet<DateOnly>? extraHolidays = null)
    {
        var nonWorking = ParseNonWorkingDays(config?.NonWorkingDays);
        if (nonWorking.Contains(date.DayOfWeek))
            return false;

        return !IsOfficialHoliday(date, extraHolidays);
    }

    public static bool IsOfficialHoliday(DateOnly date, IReadOnlySet<DateOnly>? extraHolidays = null) =>
        extraHolidays?.Contains(date) == true;

    public static int CountWorkingDays(
        DateOnly start,
        DateOnly end,
        PayrollConfiguration? config = null,
        IReadOnlySet<DateOnly>? extraHolidays = null)
    {
        if (end < start)
            return 0;

        var count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (IsWorkingDay(d, config, extraHolidays))
                count++;
        }

        return count;
    }

    /// <summary>Backward-compatible overload for tests.</summary>
    public static int CountWorkingDays(DateOnly start, DateOnly end, IReadOnlySet<DateOnly>? extraHolidays) =>
        CountWorkingDays(start, end, config: null, extraHolidays);

    public static decimal ProrateMonthlyAmount(decimal monthlyAmount, int workingDaysInPeriod, int absentWorkingDays)
    {
        if (monthlyAmount <= 0 || workingDaysInPeriod <= 0 || absentWorkingDays <= 0)
            return 0m;

        var dailyRate = monthlyAmount / workingDaysInPeriod;
        return decimal.Round(dailyRate * absentWorkingDays, 2, MidpointRounding.AwayFromZero);
    }

    public static string? ValidatePayrollPeriodCoverage(
        DateOnly periodStart,
        DateOnly periodEnd,
        PayrollConfiguration? config = null,
        IReadOnlySet<DateOnly>? extraHolidays = null)
    {
        if (periodEnd < periodStart)
            return "نهاية الفترة يجب ألا تسبق بدايتها.";

        if (CountWorkingDays(periodStart, periodEnd, config, extraHolidays) == 0)
            return "الفترة لا تحتوي على أي أيام عمل (عطل نهاية الأسبوع/رسمية).";

        return null;
    }
}
