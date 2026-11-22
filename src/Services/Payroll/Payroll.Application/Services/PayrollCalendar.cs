using HRManagement.ServiceDefaults.Localization;
using Microsoft.Extensions.Configuration;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-53 (P-6): «today» and «the current month» for the payroll rules are dates of the configured zone (<c>Localization:TimeZone</c>,
/// Asia/Damascus by default), read from an injected <see cref="TimeProvider"/>. Never <c>DateTime.UtcNow.Date</c>: after 21:00 UTC on
/// the last day of a month it is already the first of the next month in Damascus.
/// </summary>
public interface IPayrollCalendar
{
    DateTime UtcNow { get; }

    /// <summary>The calendar date now, in the configured zone.</summary>
    DateOnly Today { get; }

    /// <summary>The first day of the month <see cref="Today"/> is in.</summary>
    DateOnly FirstDayOfCurrentMonth { get; }
}

public sealed class PayrollCalendar(TimeProvider time, IConfiguration? configuration = null) : IPayrollCalendar
{
    private readonly TimeZoneInfo _zone = HrLocalTime.ResolveZone(configuration?[HrLocalTime.TimeZoneKey]);

    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(HrLocalTime.ToLocal(UtcNow, _zone));

    public DateOnly FirstDayOfCurrentMonth
    {
        get
        {
            var today = Today;
            return new DateOnly(today.Year, today.Month, 1);
        }
    }
}
