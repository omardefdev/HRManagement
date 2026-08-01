using Payroll.Domain.Entities;

namespace Payroll.Domain.Services;

public static class EmployeePayProfileRules
{
    public static decimal ResolveBaseSalary(EmployeePayProfile profile, PayGrade? grade) =>
        profile.BaseSalary > 0 ? profile.BaseSalary : grade?.BaseSalary ?? 0;

    public static int CountEmployeeWorkingDays(
        DateOnly periodStart,
        DateOnly periodEnd,
        DateTime profileFrom,
        DateTime? profileTo,
        PayrollConfiguration? config = null,
        IReadOnlySet<DateOnly>? extraHolidays = null)
    {
        var empStart = DateOnly.FromDateTime(profileFrom);
        var empEnd = profileTo.HasValue ? DateOnly.FromDateTime(profileTo.Value) : periodEnd;

        var start = periodStart > empStart ? periodStart : empStart;
        var end = periodEnd < empEnd ? periodEnd : empEnd;

        if (end < start) return 0;
        return PayrollPeriodRules.CountWorkingDays(start, end, config, extraHolidays);
    }

    public static EmployeePayProfileHistory CreateHistorySnapshot(
        EmployeePayProfile profile,
        string reason) =>
        new()
        {
            EmployeeId = profile.EmployeeId,
            PayGradeId = profile.PayGradeId,
            BaseSalary = profile.BaseSalary,
            EffectiveFrom = profile.EffectiveFrom,
            EffectiveTo = profile.EffectiveTo,
            ChangeReason = reason,
            SourceProfileId = profile.Id
        };
}
