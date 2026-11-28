using HRManagement.ServiceDefaults.Localization;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>
/// PLAN-60 (§5, D-SEP-6): «today» for a separation is Damascus's date (UTC+3 all year: between 21:00 and 24:00 UTC it is already
/// tomorrow's), never UTC's. The effective date, the decision's issue date and the execution judge it here.
/// </summary>
public sealed class SeparationClock(ICalendarService calendar, TimeProvider time)
{
    public DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    public DateOnly Today => DateOnly.FromDateTime(calendar.ToLocalTime(time.GetUtcNow()).DateTime);
}
