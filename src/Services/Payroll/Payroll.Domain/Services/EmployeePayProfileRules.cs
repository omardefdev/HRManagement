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

    /// <summary>
    /// PLAN-53 (D-53-2): a salary against a grade's bounds, with the contract consumer's rule (<c>salary &lt; min || salary &gt; max</c>);
    /// a bound that is not set (zero) is not applied.
    /// </summary>
    public static bool IsOutsideGradeBounds(decimal salary, decimal minSalary, decimal maxSalary) =>
        (minSalary > 0 && salary < minSalary) || (maxSalary > 0 && salary > maxSalary);

    /// <summary>
    /// PLAN-53 (§4 rule 1): the active pay profiles whose own salary is outside the bounds of their grade, as a query predicate. New
    /// bounds change no profile: such a profile is shown (the pay profiles screen's filter), never changed automatically. A profile with
    /// no salary of its own takes the grade's base, which is inside by the grade's own rule.
    /// </summary>
    public static readonly System.Linq.Expressions.Expression<Func<EmployeePayProfile, bool>> OutsideGradeBounds =
        p => p.IsActive && p.PayGrade != null && p.BaseSalary > 0
             && ((p.PayGrade.MinSalary > 0 && p.BaseSalary < p.PayGrade.MinSalary)
                 || (p.PayGrade.MaxSalary > 0 && p.BaseSalary > p.PayGrade.MaxSalary));

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
