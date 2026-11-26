using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payroll.Application.Abstractions;
using Payroll.Application.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Application.Services;

/// <summary>What the attendance handover says of a run: who is not closed, and the carried differences applied and pending.</summary>
public sealed record PayrollAttendanceStatus(
    PayrollAttendanceGateMode GateMode,
    bool Applies,
    int EmployeesInScope,
    IReadOnlyList<Guid> MissingAttendance,
    int CarryForwardApplied,
    int CarryForwardPending);

/// <summary>The gate's answer before a calculation: a refusal (nothing changes), or a warning to write with the calculation's start.</summary>
public sealed record PayrollAttendanceGateDecision(string? Refusal, int Missing, PayrollAuditRecord? Warning, bool WarningIsNew)
{
    /// <summary>The run's warning of an earlier calculation, when nobody is missing any more: the caller removes it with its first save.</summary>
    public PayrollAuditRecord? StaleWarning { get; init; }
}

/// <summary>
/// PLAN-57 (§4 rule 7, §5): before a regular run is calculated, payroll knows which employees' attendance is not closed for its
/// period. «Not closed» is an employee in the run's scope (an active pay profile: who the run gives a line to) with no
/// <see cref="AttendanceSummaryReceipt"/> whose period overlaps the run's.
/// <list type="bullet">
/// <item><c>Warn</c> (the default, D-LVA-10): the calculation goes on, and one <b>open</b> <see cref="PayrollAuditRecord"/> of the run
/// carries the count and the list. It is never a closed record: a closed record is what lets a run be posted.</item>
/// <item><c>Block</c>: the start is refused with the count, and the run stays as it was.</item>
/// <item><c>Off</c>: no check.</item>
/// </list>
/// It applies to regular runs only: a correction run and a final settlement are exempt (PAY-H, PLAN-64).
/// </summary>
public sealed class PayrollAttendanceGate(IPayrollRepository repository, IOptions<PayrollAttendanceOptions> options)
{
    /// <summary>The first words of the warning record's findings: how the record is found again, and told from an auditor's review.</summary>
    public const string WarningPrefix = "تحذير بوابة الحضور";

    public PayrollAttendanceGateMode Mode => options.Value.Gate;

    public static bool AppliesTo(PayrollRun run) => run.RunType == PayrollRunType.Regular;

    public static string BlockMessage(int missing, DateOnly periodStart) =>
        $"لا يبدأ الحساب: {missing} موظف في نطاق التشغيل لم يُغلق حضورهم لفترة {periodStart:yyyy-MM}. "
        + "أغلق فترة الحضور لهم ثم أعد الحساب (الإعداد Payroll:Attendance:Gate=Block).";

    /// <summary>The employees of the run's scope whose attendance is not closed for its period, in a stable order.</summary>
    public async Task<(int InScope, List<Guid> Missing)> MissingAsync(PayrollRun run, CancellationToken ct = default)
    {
        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(run.PeriodEnd);
        // The run's scope is who the run gives a line to: every employee with an active pay profile (StartPayrollRunJobHandler reads
        // the same set). An employee payroll pays and HR.Core holds no closed attendance for is «not closed», whatever the reason —
        // also when HR.Core will never close the month for him (hired after it, by HR.Core's dates): paying him for that month is
        // what the warning is there to show.
        var inScope = await repository.EmployeePayProfiles.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => p.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        var closed = (await repository.AttendanceSummaryReceipts.AsNoTracking()
                .Where(r => r.PeriodStart <= periodEnd && r.PeriodEnd >= periodStart)
                .Select(r => r.EmployeeId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        return (inScope.Count, inScope.Where(id => !closed.Contains(id)).Order().ToList());
    }

    /// <summary>
    /// The gate, for the start of a calculation. It writes nothing itself: a warning is returned (a new record, not yet added, or the
    /// run's own warning record with its findings refreshed) for the caller to save with the calculation's first save.
    /// </summary>
    public async Task<PayrollAttendanceGateDecision> EvaluateAsync(PayrollRun run, CancellationToken ct = default)
    {
        if (Mode == PayrollAttendanceGateMode.Off || !AppliesTo(run))
            return new PayrollAttendanceGateDecision(null, 0, null, false);

        var (_, missing) = await MissingAsync(run, ct);
        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        if (missing.Count > 0 && Mode == PayrollAttendanceGateMode.Block)
            return new PayrollAttendanceGateDecision(BlockMessage(missing.Count, periodStart), missing.Count, null, false);

        // One warning record per run, however often it is calculated: a recalculation refreshes it, and removes it when the
        // attendance was closed meanwhile (the record says what this calculation ran without).
        var warning = await repository.PayrollAuditRecords
            .Where(a => a.PayrollRunId == run.Id && a.Status == AuditStatus.Open && a.Findings != null && a.Findings.StartsWith(WarningPrefix))
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (missing.Count == 0)
            return new PayrollAttendanceGateDecision(null, 0, null, false) { StaleWarning = warning };

        var isNew = warning is null;
        warning ??= new PayrollAuditRecord { PayrollRunId = run.Id, Status = AuditStatus.Open, TenantId = run.TenantId };
        warning.Findings = Findings(missing, periodStart);
        return new PayrollAttendanceGateDecision(null, missing.Count, warning, isNew);
    }

    /// <summary>
    /// Whether an audit record is the gate's warning. It is not a review: it is never closed by hand, and a closed one would not let
    /// a run be posted (<c>PostPayrollRunHandler</c> looks for a closed record that is not this).
    /// </summary>
    public static bool IsWarning(PayrollAuditRecord record) => IsWarning(record.Findings);

    public static bool IsWarning(string? findings) => findings is not null && findings.StartsWith(WarningPrefix, StringComparison.Ordinal);

    /// <summary>The warning's sentence without its list of ids (lists show it; the run's page names the employees).</summary>
    public static string Headline(string? findings)
    {
        var line = (findings ?? string.Empty).Split('\n')[0];
        const string listIntro = " معرّفات الموظفين:";
        return line.EndsWith(listIntro, StringComparison.Ordinal) ? line[..^listIntro.Length] : line;
    }

    /// <summary>The warning's text: the count, the period, then the employees' ids, one per line.</summary>
    public static string Findings(IReadOnlyList<Guid> missing, DateOnly periodStart)
    {
        var text = new StringBuilder();
        text.Append(WarningPrefix).Append(": ").Append(missing.Count)
            .Append(" موظف في نطاق التشغيل لم يُغلق حضورهم لفترة ").Append(periodStart.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture))
            .Append(". حُسب التشغيل بلا خصم غياب لهم (الإعداد Payroll:Attendance:Gate=Warn). معرّفات الموظفين:");
        foreach (var id in missing)
            text.Append('\n').Append(id.ToString("D"));
        return text.ToString();
    }

    /// <summary>The ids a warning record lists (its findings after the first line).</summary>
    public static IReadOnlyList<Guid> ParseFindings(string? findings) =>
        (findings ?? string.Empty).Split('\n').Skip(1).Select(l => Guid.TryParse(l.Trim(), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();

    /// <summary>What the run's page and <c>GET api/payroll/runs/{id}/attendance-status</c> show.</summary>
    public async Task<PayrollAttendanceStatus> StatusAsync(PayrollRun run, CancellationToken ct = default)
    {
        var applies = AppliesTo(run);
        var (inScope, missing) = applies ? await MissingAsync(run, ct) : (0, []);
        var periodStart = DateOnly.FromDateTime(run.PeriodStart);

        // Applied: the carried differences of an earlier period this run took. Pending: those still free for a later regular run.
        var applied = await repository.AbsenceDeductions.AsNoTracking()
            .CountAsync(a => a.AppliedInRunId == run.Id && a.OriginalPeriodEnd != null && a.OriginalPeriodEnd < periodStart, ct);
        var pending = await repository.AbsenceDeductions.AsNoTracking()
            .CountAsync(a => a.AppliedInRunId == null && a.CarryForward && a.OriginalPeriodEnd != null, ct);
        return new PayrollAttendanceStatus(Mode, applies, inScope, missing, applied, pending);
    }
}
