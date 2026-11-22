using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

namespace Payroll.Application.Features.PayProfileChanges;

/// <summary>
/// PLAN-53 (D-53-1, D-53-2, P-6): the rules a salary change meets at the request, and again at the approval and at the activation. They
/// read; they write nothing.
/// </summary>
public sealed class PayProfileChangeRules(
    IPayrollRepository repository,
    IPayrollCalendar calendar,
    IOptions<PayProfileChangeOptions> options)
{
    public PayProfileChangeOptions Options => options.Value;

    /// <summary>D-53-1 at the request: the first day of a month, and not before the current month (dates of the configured zone).</summary>
    public string? CheckRequestedDate(DateOnly effectiveFrom)
    {
        if (options.Value.RequireMonthStart && effectiveFrom.Day != 1)
            return PayProfileChangeMessages.MonthStartRequired;
        if (effectiveFrom < calendar.FirstDayOfCurrentMonth)
            return PayProfileChangeMessages.BeforeCurrentMonth;
        return null;
    }

    /// <summary>
    /// D-53-2: the salary the change leaves against the bounds of the grade it leaves (the proposed grade, else the current one), with the
    /// contract consumer's rule (<c>salary &lt; min || salary &gt; max</c>); a bound that is not set (zero) is not applied. The message
    /// names both bounds.
    /// </summary>
    public async Task<string?> CheckBoundsAsync(decimal salary, Guid? gradeId, CancellationToken ct)
    {
        if (options.Value.AllowOutOfGradeBounds || gradeId is null)
            return null;
        var grade = await repository.PayGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gradeId, ct);
        if (grade is null)
            return PayProfileChangeMessages.GradeNotFound;
        var below = grade.MinSalary > 0 && salary < grade.MinSalary;
        var above = grade.MaxSalary > 0 && salary > grade.MaxSalary;
        return below || above
            ? PayProfileChangeMessages.OutOfGradeBounds(grade.GradeCode, grade.MinSalary, grade.MaxSalary)
            : null;
    }

    /// <summary>
    /// P-6: «a month whose run is frozen, approved or posted». A run blocks a change when its period contains the effective date, it is
    /// neither reversed nor cancelled, it covers the employee (a Regular run covers every employee; a run of another type covers the
    /// employees on its salary lines), and its results are frozen, or it is approved or posted, or it is a Regular run being calculated or
    /// validated. A change contradicts only a result that holds this employee.
    /// </summary>
    public async Task<string?> FindBlockingRunAsync(Guid employeeId, DateOnly effectiveFrom, CancellationToken ct)
    {
        var date = EmployeePayProfileVersioning.ToStoredDate(effectiveFrom);
        var runs = await repository.PayrollRuns.AsNoTracking()
            .Where(r => r.PeriodStart <= date && r.PeriodEnd >= date)
            .Where(r => r.Status != PayrollRunStatus.Reversed && r.Status != PayrollRunStatus.Cancelled)
            .Select(r => new { r.Id, r.RunType, r.Status, r.ResultsFrozen })
            .ToListAsync(ct);

        foreach (var run in runs)
        {
            var settled = run.ResultsFrozen || run.Status is PayrollRunStatus.Approved or PayrollRunStatus.Posted;
            var computing = run.RunType == PayrollRunType.Regular
                            && run.Status is PayrollRunStatus.Calculating or PayrollRunStatus.Validating;
            if (!settled && !computing)
                continue;
            var covers = run.RunType == PayrollRunType.Regular
                         || await repository.SalaryLines.AsNoTracking().AnyAsync(l => l.PayrollRunId == run.Id && l.EmployeeId == employeeId, ct);
            if (covers)
                return PayProfileChangeMessages.FrozenMonth(effectiveFrom);
        }

        return null;
    }

    /// <summary>The kind a proposal has against the current profile, or null when it changes nothing.</summary>
    public static PayProfileChangeKind? KindOf(EmployeePayProfile current, decimal? proposedBaseSalary, Guid? proposedPayGradeId)
    {
        var salary = proposedBaseSalary is decimal s && s != current.BaseSalary;
        var grade = proposedPayGradeId is Guid g && g != current.PayGradeId;
        return (salary, grade) switch
        {
            (true, true) => PayProfileChangeKind.BaseSalaryAndGrade,
            (true, false) => PayProfileChangeKind.BaseSalary,
            (false, true) => PayProfileChangeKind.PayGrade,
            _ => null,
        };
    }
}
