using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>
/// PLAN-57 (§4 rule 4): the one place where the attendance handover reads the payroll run's states (PLAN-56's guards). A period is
/// what an attendance summary names; its run is a <b>regular</b> run whose period overlaps it and that is neither cancelled nor
/// reversed.
/// <list type="bullet">
/// <item><see cref="HasCalculatedRunAsync"/>: such a run has been calculated (it is past <c>Draft</c>). A difference that arrives
/// now is carried: the run's recalculation takes it if one comes before the freeze, otherwise the first later regular run does.</item>
/// <item><see cref="IsFrozenAsync"/>: such a run's results are frozen, or it is under review, approved or posted. Its figures never
/// change; a difference of its period can only be applied by a later run.</item>
/// </list>
/// A correction run and a final settlement never decide either answer.
/// </summary>
public static class PayrollAttendanceFreeze
{
    /// <summary>The live regular runs whose period overlaps [<paramref name="periodStart"/>, <paramref name="periodEnd"/>].</summary>
    public static IQueryable<PayrollRun> LiveRegularRuns(IQueryable<PayrollRun> runs, DateOnly periodStart, DateOnly periodEnd)
    {
        var from = periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = periodEnd.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        return runs.Where(r => r.RunType == PayrollRunType.Regular
                               && r.Status != PayrollRunStatus.Cancelled
                               && r.Status != PayrollRunStatus.Reversed
                               && r.PeriodStart <= to
                               && r.PeriodEnd >= from);
    }

    /// <summary>The run's state reads as «calculated»: anything past the draft a calculation starts from.</summary>
    public static bool IsCalculated(PayrollRunStatus status) =>
        status is not (PayrollRunStatus.Draft or PayrollRunStatus.Cancelled or PayrollRunStatus.Reversed);

    /// <summary>The run's figures are fixed: validated (<c>ResultsFrozen</c>), or handed to the review and beyond.</summary>
    public static bool IsFrozen(PayrollRunStatus status, bool resultsFrozen) =>
        status is not (PayrollRunStatus.Cancelled or PayrollRunStatus.Reversed)
        && (resultsFrozen || status is PayrollRunStatus.UnderReview or PayrollRunStatus.Approved or PayrollRunStatus.Posted);

    public static Task<bool> HasCalculatedRunAsync(
        IPayrollRepository repository, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) =>
        HasCalculatedRunAsync(repository.PayrollRuns, periodStart, periodEnd, ct);

    public static async Task<bool> HasCalculatedRunAsync(
        IQueryable<PayrollRun> runs, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) =>
        await LiveRegularRuns(runs.AsNoTracking(), periodStart, periodEnd).AnyAsync(r => r.Status != PayrollRunStatus.Draft, ct);

    public static Task<bool> IsFrozenAsync(
        IPayrollRepository repository, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) =>
        IsFrozenAsync(repository.PayrollRuns, periodStart, periodEnd, ct);

    public static async Task<bool> IsFrozenAsync(
        IQueryable<PayrollRun> runs, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default) =>
        await LiveRegularRuns(runs.AsNoTracking(), periodStart, periodEnd)
            .AnyAsync(r => r.ResultsFrozen
                           || r.Status == PayrollRunStatus.UnderReview
                           || r.Status == PayrollRunStatus.Approved
                           || r.Status == PayrollRunStatus.Posted, ct);
}
