using System.Globalization;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Attendance;

public enum AttendanceCloseOutcome
{
    /// <summary>Done (also when everyone in scope was closed already: nothing was written).</summary>
    Closed = 0,
    /// <summary>The month, or the custom bounds, cannot be read, overlap a period, or have not ended.</summary>
    InvalidPeriod,
    /// <summary>The unit does not exist in the caller's agency.</summary>
    UnitNotFound,
    /// <summary>The unit is outside the caller's scope, or a branch asked for the whole agency.</summary>
    OutOfScope,
    /// <summary>An employee in scope has a working day with no record, and the setting requires a complete month. Nothing was written.</summary>
    Incomplete,
    /// <summary>Another close wrote a summary of the same period at the same time. Nothing of this close was written.</summary>
    ConcurrentClose
}

public sealed record AttendanceCloseIncomplete(Guid EmployeeId, int UnrecordedWorkingDays);

public sealed record AttendanceCloseResult(
    AttendanceCloseOutcome Outcome,
    string? Error,
    Guid? PeriodId,
    AttendancePeriodStatus? Status,
    int ClosedEmployees,
    int SkippedAlreadyClosed,
    IReadOnlyList<AttendanceCloseIncomplete> Incomplete)
{
    public bool Ok => Outcome == AttendanceCloseOutcome.Closed;

    public static AttendanceCloseResult Refused(AttendanceCloseOutcome outcome, string error) => new(outcome, error, null, null, 0, 0, []);
}

public sealed record AttendanceSummaryView(
    Guid SummaryId,
    Guid EmployeeId,
    int Version,
    int WorkingDays,
    int PresentDays,
    int AbsentDays,
    int OnLeaveDays,
    int RemoteDays,
    int UnrecordedWorkingDays,
    AttendanceSummaryReason LastReason);

/// <summary>A period as its caller may see it: its state, and the closed and remaining employees of the caller's scope.</summary>
public sealed record AttendancePeriodView(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? PeriodId,
    AttendancePeriodStatus Status,
    int ClosedEmployees,
    int RemainingEmployees,
    IReadOnlyList<AttendanceSummaryView> Summaries);

public static class AttendancePeriodMessages
{
    public const string InvalidMonth = "صيغة الشهر غير صالحة. المطلوب yyyy-MM.";
    public const string InvalidBounds = "حدود الفترة غير صالحة: يجب أن تسبق البداية النهاية.";
    public const string NotCalendarMonth = "الفترة شهر تقويمي كامل. الفترات المخصصة متاحة بالإعداد Attendance:Periods:Mode=Custom فقط.";
    public const string Overlap = "الفترة تتداخل مع فترة حضور قائمة لهذه الجهة.";
    public const string NotEnded = "لا تُغلق فترة الحضور قبل انتهائها.";
    public const string UnitNotFound = "الوحدة التنظيمية غير موجودة.";
    public const string UnitOutOfScope = "الوحدة التنظيمية خارج نطاق صلاحيتك.";
    public const string WholeAgencyNeedsCentral = "إغلاق الجهة كلها للمستخدم المركزي فقط؛ اختر وحدة من نطاقك.";
    public const string Concurrent = "أُغلقت الفترة لبعض الموظفين بالتوازي؛ أعد المحاولة";

    public static string Incomplete(int employees) =>
        $"لا يمكن إغلاق الفترة: {employees} موظف له أيام عمل بلا سجل حضور. أكمل السجلات (الإدخال الجماعي يملأ أيام العمل) ثم أعد الإغلاق.";
}

/// <summary>
/// PLAN-57 (§1): the attendance period and its close.
/// <list type="number">
/// <item>The period is a calendar month (D-LVA-11), created at its first close. Two periods of an agency never overlap.</item>
/// <item>The employees of a close: <see cref="AttendanceEligibility"/> (the unit's tree, hired by the period's end, in service or
/// whose service ended inside the period).</item>
/// <item>A branch closes its own tree only; a central user closes any unit or the whole agency.</item>
/// <item>The summary is counted by <see cref="AttendanceSummaryCalculator"/>, on working days only.</item>
/// <item>With <c>RequireComplete</c> (D-LVA-9) the whole close is refused when anyone in scope has a working day with no record.</item>
/// <item>A close is idempotent: an employee who has a summary in the period is skipped and counted. Two concurrent closes meet on
/// the summary's unique index, and the loser writes nothing.</item>
/// <item><b>Atomicity:</b> every summary, the closure row and every <see cref="IAttendanceSummaryPublished"/> are written by one
/// save; the events are staged through the outbox before it (PLAN-40).</item>
/// <item>The period is <c>Closed</c> when no eligible employee of the agency lacks a summary, otherwise <c>PartiallyClosed</c>; that
/// is for display. The lock is the employee's summary (<see cref="AttendanceLockService"/>).</item>
/// <item>There is no reopening: a change after the close is a correction.</item>
/// </list>
/// </summary>
public sealed class AttendancePeriodService(
    HRCoreDbContext db,
    IPublishEndpoint publish,
    AttendanceEligibility eligibility,
    AttendanceCalendar calendar,
    IOptions<AttendanceOptions> options)
{
    public const string SourceSystem = "HR.Core";

    private readonly AttendanceOptions _options = options.Value;

    public AttendancePeriodMode Mode => _options.Periods.Mode;

    public AttendanceUnrecordedDaysMode UnrecordedMode => _options.Close.UnrecordedWorkingDays;

    /// <summary>Reads <c>yyyy-MM</c> as the calendar month's bounds.</summary>
    public static bool TryParseMonth(string? month, out DateOnly start, out DateOnly end)
    {
        start = default;
        end = default;
        if (string.IsNullOrWhiteSpace(month)
            || !DateOnly.TryParseExact(month.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
            return false;
        end = start.AddMonths(1).AddDays(-1);
        return true;
    }

    /// <summary>The bounds a close may take: a whole calendar month, or any bounds when the mode is <c>Custom</c>.</summary>
    public string? RefusePeriod(DateOnly start, DateOnly end)
    {
        if (end < start)
            return AttendancePeriodMessages.InvalidBounds;
        var wholeMonth = start.Day == 1 && end == start.AddMonths(1).AddDays(-1);
        if (Mode == AttendancePeriodMode.CalendarMonth && !wholeMonth)
            return AttendancePeriodMessages.NotCalendarMonth;
        if (end > calendar.Today)
            return AttendancePeriodMessages.NotEnded;
        return null;
    }

    /// <summary>The stored period of these bounds, and whether the bounds overlap another period of the agency (rule 1).</summary>
    private async Task<(AttendancePeriod? Period, bool Overlaps)> FindPeriodAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var touching = await db.AttendancePeriods.Where(p => p.PeriodStart <= end && p.PeriodEnd >= start).ToListAsync(ct);
        var same = touching.Count == 1 && touching[0].PeriodStart == start && touching[0].PeriodEnd == end ? touching[0] : null;
        return (same, same is null && touching.Count > 0);
    }

    /// <summary>
    /// The period of these bounds: the stored one, or a new <c>Open</c> one added to the context (not saved: the caller's save
    /// writes it). Null when the bounds overlap another period of the agency.
    /// </summary>
    public async Task<AttendancePeriod?> EnsurePeriodAsync(DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        var (period, overlaps) = await FindPeriodAsync(start, end, ct);
        if (overlaps)
            return null;
        if (period is not null)
            return period;

        period = new AttendancePeriod { PeriodStart = start, PeriodEnd = end, Status = AttendancePeriodStatus.Open, TenantId = db.EffectiveTenantId };
        db.AttendancePeriods.Add(period);
        return period;
    }

    /// <summary>Closes a calendar month for a unit's tree, or for the whole agency when <paramref name="orgUnitId"/> is null.</summary>
    public Task<AttendanceCloseResult> CloseAsync(DateOnly monthStart, Guid? orgUnitId, AttendanceActor actor, CancellationToken ct = default) =>
        CloseAsync(new DateOnly(monthStart.Year, monthStart.Month, 1), new DateOnly(monthStart.Year, monthStart.Month, 1).AddMonths(1).AddDays(-1),
            orgUnitId, actor, ct);

    public async Task<AttendanceCloseResult> CloseAsync(
        DateOnly periodStart, DateOnly periodEnd, Guid? orgUnitId, AttendanceActor actor, CancellationToken ct = default)
    {
        if (RefusePeriod(periodStart, periodEnd) is { } badPeriod)
            return AttendanceCloseResult.Refused(AttendanceCloseOutcome.InvalidPeriod, badPeriod);

        // Rule 3: a unit of another agency does not exist here (404); a unit of this agency outside the scope is refused (403).
        if (orgUnitId is Guid unit)
        {
            if (await eligibility.UnitTreePathAsync(unit, ct) is null)
                return AttendanceCloseResult.Refused(AttendanceCloseOutcome.UnitNotFound, AttendancePeriodMessages.UnitNotFound);
            if (!actor.Scope.CanAccess(unit))
                return AttendanceCloseResult.Refused(AttendanceCloseOutcome.OutOfScope, AttendancePeriodMessages.UnitOutOfScope);
        }
        else if (!actor.Scope.IsUnrestricted)
        {
            return AttendanceCloseResult.Refused(AttendanceCloseOutcome.OutOfScope, AttendancePeriodMessages.WholeAgencyNeedsCentral);
        }

        // The month's lock, exclusively, before a day is read (AttendanceMonthLock): a save of an attendance day that began earlier
        // has finished and is counted below; one that begins now waits for this close and is refused. Released with the transaction.
        await using var transaction = await AttendanceMonthLock.BeginAsync(db, ct);
        await AttendanceMonthLock.TakeAsync(db, AttendanceMonthLock.Keys(db.EffectiveTenantId, periodStart, periodEnd), exclusive: true, ct);

        var (period, overlaps) = await FindPeriodAsync(periodStart, periodEnd, ct);
        if (overlaps)
            return AttendanceCloseResult.Refused(AttendanceCloseOutcome.InvalidPeriod, AttendancePeriodMessages.Overlap);

        // Rule 2 and rule 6: the employees in scope, less those an earlier close gave a summary.
        var inScope = await eligibility.GetAsync(orgUnitId, periodStart, periodEnd, ct);
        var summarized = period is null
            ? []
            : (await db.AttendanceEmployeeSummaries.AsNoTracking()
                .Where(s => s.AttendancePeriodId == period.Id)
                .Select(s => s.EmployeeId)
                .ToListAsync(ct)).ToHashSet();
        var toClose = inScope.Where(e => !summarized.Contains(e.Id)).ToList();
        var skipped = inScope.Count - toClose.Count;

        if (toClose.Count == 0)
        {
            // Nothing to close: the same answer as the close that did the work, with no new closure row and no event. The period's
            // state is read again all the same: two closes of two units, each sure the other's employees were still open, can both
            // have left «partially closed» on a period whose last employee they closed between them.
            if (period is { Status: AttendancePeriodStatus.PartiallyClosed })
            {
                var everyone = orgUnitId is null ? inScope : await eligibility.GetAsync(null, periodStart, periodEnd, ct);
                if (everyone.All(e => summarized.Contains(e.Id)))
                {
                    var tracked = await db.AttendancePeriods.FirstAsync(p => p.Id == period.Id, ct);
                    tracked.Status = AttendancePeriodStatus.Closed;
                    tracked.ClosedAt ??= DateTime.UtcNow;
                    tracked.ClosedBy ??= actor.UserId;
                    await db.SaveChangesAsync(ct);
                    if (transaction is not null)
                        await transaction.CommitAsync(ct);
                    return new AttendanceCloseResult(AttendanceCloseOutcome.Closed, null, period.Id, AttendancePeriodStatus.Closed, 0, skipped, []);
                }
            }

            return new AttendanceCloseResult(
                AttendanceCloseOutcome.Closed, null, period?.Id, period?.Status ?? AttendancePeriodStatus.Open, 0, skipped, []);
        }

        // One query for the period's records of everyone to close.
        var ids = toClose.Select(e => e.Id).ToList();
        var records = (await db.DailyAttendanceRecords.AsNoTracking()
                .Where(r => ids.Contains(r.EmployeeId) && r.AttendanceDate >= periodStart && r.AttendanceDate <= periodEnd)
                .Select(r => new { r.EmployeeId, r.AttendanceDate, r.Status })
                .ToListAsync(ct))
            .GroupBy(r => r.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.AttendanceDate, r => r.Status));
        var none = new Dictionary<DateOnly, DailyAttendanceStatus>();

        var figures = new List<(AttendanceEligibleEmployee Employee, AttendanceSummaryFigures Figures)>(toClose.Count);
        foreach (var employee in toClose)
        {
            figures.Add((employee, AttendanceSummaryCalculator.Calculate(
                records.GetValueOrDefault(employee.Id, none), periodStart, periodEnd, calendar,
                employee.HireDate, employee.EndOfService, UnrecordedMode)));
        }

        // Rule 5: with RequireComplete the whole close is refused, and nothing is written (the period is not created either).
        if (UnrecordedMode == AttendanceUnrecordedDaysMode.RequireComplete)
        {
            var incomplete = figures
                .Where(f => f.Figures.UnrecordedWorkingDays > 0)
                .Select(f => new AttendanceCloseIncomplete(f.Employee.Id, f.Figures.UnrecordedWorkingDays))
                .ToList();
            if (incomplete.Count > 0)
            {
                return new AttendanceCloseResult(
                    AttendanceCloseOutcome.Incomplete, AttendancePeriodMessages.Incomplete(incomplete.Count), period?.Id, period?.Status, 0, skipped, incomplete);
            }
        }

        var tenantId = db.EffectiveTenantId;
        var now = DateTime.UtcNow;
        // The period is created here, after every refusal, so that a refused close leaves nothing for a later save.
        period ??= (await EnsurePeriodAsync(periodStart, periodEnd, ct))!;

        var correlationId = CorrelationIdFlow.Get();
        foreach (var (employee, f) in figures)
        {
            var summary = new AttendanceEmployeeSummary
            {
                AttendancePeriodId = period.Id,
                EmployeeId = employee.Id,
                Version = 1,
                WorkingDays = f.WorkingDays,
                PresentDays = f.PresentDays,
                AbsentDays = f.AbsentDays,
                OnLeaveDays = f.OnLeaveDays,
                RemoteDays = f.RemoteDays,
                UnrecordedWorkingDays = f.UnrecordedWorkingDays,
                LastReason = AttendanceSummaryReason.Close,
                PublishedAt = now,
                TenantId = tenantId
            };
            db.AttendanceEmployeeSummaries.Add(summary);
            await PublishSummaryAsync(summary, period, null, now, correlationId, ct);
        }

        db.AttendancePeriodClosures.Add(new AttendancePeriodClosure
        {
            AttendancePeriodId = period.Id,
            OrgUnitId = orgUnitId,
            ClosedBy = actor.UserId,
            ClosedAt = now,
            EmployeeCount = figures.Count,
            SkippedAlreadyClosed = skipped,
            TenantId = tenantId
        });

        // Rule 8: closed when no eligible employee of the agency lacks a summary.
        var closedNow = summarized.Concat(ids).ToHashSet();
        var agency = orgUnitId is null ? inScope : await eligibility.GetAsync(null, periodStart, periodEnd, ct);
        var everyoneClosed = agency.All(e => closedNow.Contains(e.Id));
        period.Status = everyoneClosed ? AttendancePeriodStatus.Closed : AttendancePeriodStatus.PartiallyClosed;
        if (everyoneClosed)
        {
            period.ClosedAt ??= now;
            period.ClosedBy ??= actor.UserId;
        }

        try
        {
            // Rule 7: the one save. The summaries, the closure, the period's state and the outbox messages are written together.
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // Rule 6: another close wrote a summary of this period (or the period itself) first. This save wrote nothing.
            db.ChangeTracker.Clear();
            return AttendanceCloseResult.Refused(AttendanceCloseOutcome.ConcurrentClose, AttendancePeriodMessages.Concurrent);
        }

        return new AttendanceCloseResult(AttendanceCloseOutcome.Closed, null, period.Id, period.Status, figures.Count, skipped, []);
    }

    /// <summary>
    /// Stages one version of a summary for payroll. It does not save: the caller's single save writes the message with the summary.
    /// </summary>
    [StagesOutboxMessages]
    public Task PublishSummaryAsync(
        AttendanceEmployeeSummary summary,
        AttendancePeriod period,
        Guid? correctionId,
        DateTime occurredAt,
        string? correlationId,
        CancellationToken ct = default) =>
        publish.Publish<IAttendanceSummaryPublished>(new
        {
            SummaryId = summary.Id,
            summary.Version,
            AttendancePeriodId = period.Id,
            period.PeriodStart,
            period.PeriodEnd,
            summary.EmployeeId,
            summary.WorkingDays,
            summary.PresentDays,
            summary.AbsentDays,
            summary.OnLeaveDays,
            summary.RemoteDays,
            summary.UnrecordedWorkingDays,
            Reason = summary.LastReason.ToString(),
            CorrectionId = correctionId,
            TenantId = (Guid?)(summary.TenantId == Guid.Empty ? db.EffectiveTenantId : summary.TenantId),
            OccurredAt = occurredAt,
            CorrelationId = correlationId,
            SourceSystem
        }, ct);

    /// <summary>
    /// A month as the caller may see it: the period's state, and the closed and remaining employees of his scope with the latest
    /// version of each summary. A branch sees its tree only.
    /// </summary>
    public async Task<AttendancePeriodView> GetStatusAsync(DateOnly monthStart, AttendanceActor actor, CancellationToken ct = default)
    {
        var start = new DateOnly(monthStart.Year, monthStart.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return await GetStatusAsync(start, end, actor, ct);
    }

    public async Task<AttendancePeriodView> GetStatusAsync(DateOnly start, DateOnly end, AttendanceActor actor, CancellationToken ct = default)
    {
        var period = await db.AttendancePeriods.AsNoTracking().FirstOrDefaultAsync(p => p.PeriodStart == start && p.PeriodEnd == end, ct);
        if (actor.Scope.IsDenied)
            return new AttendancePeriodView(start, end, period?.Id, period?.Status ?? AttendancePeriodStatus.Open, 0, 0, []);

        var eligible = await eligibility.GetAsync(null, start, end, ct);
        if (actor.Scope.MustFilter)
            eligible = eligible.Where(e => actor.Scope.CanAccess(e.PrimaryOrgUnitId)).ToList();
        var eligibleIds = eligible.Select(e => e.Id).ToHashSet();

        var summaries = new List<AttendanceSummaryView>();
        if (period is not null)
        {
            var query = db.AttendanceEmployeeSummaries.AsNoTracking().Where(s => s.AttendancePeriodId == period.Id);
            if (actor.Scope.MustFilter)
            {
                query = query.Where(s => s.Employee!.PrimaryOrgUnitId != null
                                         && actor.Scope.AllowedOrgUnitIds.Contains(s.Employee.PrimaryOrgUnitId.Value));
            }

            summaries = await query
                .OrderBy(s => s.EmployeeId)
                .Select(s => new AttendanceSummaryView(
                    s.Id, s.EmployeeId, s.Version, s.WorkingDays, s.PresentDays, s.AbsentDays, s.OnLeaveDays, s.RemoteDays,
                    s.UnrecordedWorkingDays, s.LastReason))
                .ToListAsync(ct);
        }

        var closedIds = summaries.Select(s => s.EmployeeId).ToHashSet();
        var remaining = eligibleIds.Count(id => !closedIds.Contains(id));
        return new AttendancePeriodView(
            start, end, period?.Id, period?.Status ?? AttendancePeriodStatus.Open, summaries.Count, remaining, summaries);
    }
}
