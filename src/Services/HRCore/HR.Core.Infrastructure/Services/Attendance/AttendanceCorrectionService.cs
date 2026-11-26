using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HR.Core.Infrastructure.Services.Attendance;

public enum AttendanceCorrectionOutcome
{
    Done = 0,
    /// <summary>The correction or the employee does not exist in the caller's agency.</summary>
    NotFound,
    /// <summary>The employee is outside the caller's scope.</summary>
    OutOfScope,
    /// <summary>A rule of the request is broken (a short reason, the same status, «في إجازة» by hand, a day that is not locked).</summary>
    Invalid,
    /// <summary>The decider is the requester.</summary>
    SeparationOfDuties,
    /// <summary>A request already waits for that day, or the correction was decided already, or no longer applies.</summary>
    Conflict
}

public sealed record AttendanceCorrectionResult(AttendanceCorrectionOutcome Outcome, string? Error, AttendanceCorrection? Correction)
{
    public bool Ok => Outcome == AttendanceCorrectionOutcome.Done;

    public static AttendanceCorrectionResult Done(AttendanceCorrection correction) => new(AttendanceCorrectionOutcome.Done, null, correction);

    public static AttendanceCorrectionResult Refused(AttendanceCorrectionOutcome outcome, string error) => new(outcome, error, null);
}

public static class AttendanceCorrectionMessages
{
    public const string NotFound = "طلب التصحيح غير موجود.";
    public const string ShortReason = "سبب التصحيح مطلوب، ولا يقل عن 15 حرفاً.";
    public const string LongReason = "سبب التصحيح أطول من الحد المسموح (1000 حرف).";
    public const string DayNotLocked = "التصحيح لليوم المقفل فقط. هذا اليوم ليس ضمن فترة حضور مغلقة للموظف، ويُعدَّل مباشرة من سجل الحضور.";
    public const string SameStatus = "الحالة المطلوبة تطابق حالة اليوم الحالية؛ لا شيء يُصحَّح.";
    public const string LeaveMadeDay = "هذا اليوم إجازة معتمدة؛ يتغير بإلغاء الإجازة لا بطلب تصحيح.";
    public const string NoRequester = "تعذر تحديد هوية مقدّم الطلب.";
    public const string NoDecider = "تعذر تحديد هوية صاحب القرار.";
    public const string PendingExists = "يوجد طلب تصحيح بانتظار القرار لهذا الموظف في هذا اليوم.";
    public const string AlreadyDecided = "سبق البتّ في هذا الطلب.";
    public const string OwnRequest = "لا يبتّ مقدّم الطلب في طلبه: الاعتماد والرفض لشخص آخر.";
    public const string RejectionNeedsNote = "الرفض يتطلب ملاحظة تبيّن سببه.";
    public const string NoLongerApplies = "لم يعد الطلب ينطبق: حالة اليوم الحالية تطابق الحالة المطلوبة. ارفض الطلب بملاحظة.";
    public const string ChangedMeanwhile = "تغيّر ملخص الحضور أثناء الاعتماد أكثر من مرة؛ أعد المحاولة.";
    public const string SupersededByLeave = "استُبدل بتصحيح نظامي من قرار إجازة على اليوم نفسه.";
    public const string LeaveCancelledBeforeApproval = "أُلغيت الإجازة قبل اعتماد هذا التصحيح.";

    public static string LeaveApproved(Guid leaveRequestId) => $"إجازة معتمدة {leaveRequestId:D}";

    public static string LeaveCancelled(Guid leaveRequestId) => $"إجازة ملغاة {leaveRequestId:D}";
}

/// <summary>
/// PLAN-57 (§3): a change of a locked attendance day.
/// <list type="number">
/// <item>A correction is for a locked day only; an unlocked day is edited directly.</item>
/// <item>The reason is mandatory, at least 15 characters; the new status differs from the day's; a person cannot ask for «في إجازة».</item>
/// <item>The requester holds <c>HrCore.AttendanceWrite</c> and the employee is in his scope (checked by the writer, as for a create).</item>
/// <item>The decider holds <c>HrCore.AttendanceCorrectionApprove</c> (central) and is not the requester: the caller's subject is
/// compared with <c>RequestedBy</c> here, on the server. A rejection needs a note.</item>
/// <item><b>An approval is atomic</b>: the day (through <see cref="AttendanceDayWriter.ApplyApprovedCorrectionAsync"/>), the
/// summary recomputed by the same calculator with <c>Version</c> + 1, the staged <c>IAttendanceSummaryPublished</c> and the
/// correction's state are written by one save; when the summary changed under it (<c>xmin</c>) the approval starts again in a
/// new unit of work, from what is stored.</item>
/// <item>A correction that changes no figure still raises the version and publishes it.</item>
/// <item>A leave decision that touches a locked day makes a system correction with no requester and no decider (D-LVA-12):
/// approved in the consumer's own save under <c>AutoApply</c>, waiting for a central person under <c>Pending</c>. Every locked day
/// of the leave gets its row; the summary takes one new version for them.</item>
/// <item>Everything is written by the ordinary save, so every request and decision reaches the audit log.</item>
/// </list>
/// </summary>
public sealed class AttendanceCorrectionService(
    HRCoreDbContext db,
    AttendanceDayWriter writer,
    AttendancePeriodService periods,
    AttendanceEligibility eligibility,
    AttendanceCalendar calendar,
    IOptions<AttendanceOptions> options,
    IServiceScopeFactory? scopes = null)
{
    private const int ApprovalAttempts = 4;

    private readonly AttendanceOptions _options = options.Value;

    private static string ApprovedReference(Guid leaveRequestId) => AttendanceDayWriter.LeaveReference(leaveRequestId) + ":Approved";

    private static string CancelledReference(Guid leaveRequestId) => AttendanceDayWriter.LeaveReference(leaveRequestId) + ":Cancelled";

    // ---------------------------------------------------------------- a person's request

    public async Task<AttendanceCorrectionResult> RequestAsync(
        AttendanceActor actor, Guid employeeId, DateOnly date, DailyAttendanceStatus newStatus, string? reason, CancellationToken ct = default)
    {
        if (await writer.RefuseOutOfScopeAsync(actor, employeeId, ct) is { } refused)
        {
            return AttendanceCorrectionResult.Refused(
                refused.Refusal == AttendanceRefusal.NotFound ? AttendanceCorrectionOutcome.NotFound : AttendanceCorrectionOutcome.OutOfScope,
                refused.Error!);
        }

        if (actor.UserId is not Guid requester)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.NoRequester);

        var text = (reason ?? string.Empty).Trim();
        if (text.Length < AttendanceCorrection.ReasonMinLength)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.ShortReason);
        if (text.Length > AttendanceCorrection.ReasonMaxLength)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.LongReason);
        if (!Enum.IsDefined(newStatus))
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceMessages.UnknownStatus);
        if (newStatus == DailyAttendanceStatus.OnLeave)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceMessages.OnLeaveByHand);

        var summary = await SummaryCoveringAsync(employeeId, date, tracked: false, ct);
        if (summary is null)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.DayNotLocked);

        var record = await db.DailyAttendanceRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.AttendanceDate == date, ct);
        if (record?.Source == AttendanceRecordSource.Leave)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.LeaveMadeDay);
        if (record?.Status == newStatus)
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.SameStatus);

        if (await db.AttendanceCorrections.AsNoTracking()
                .AnyAsync(c => c.EmployeeId == employeeId && c.AttendanceDate == date && c.Status == AttendanceCorrectionStatus.Pending, ct))
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.PendingExists);

        var correction = new AttendanceCorrection
        {
            AttendancePeriodId = summary.AttendancePeriodId,
            EmployeeId = employeeId,
            AttendanceDate = date,
            OldStatus = record?.Status,
            NewStatus = newStatus,
            Reason = text,
            Source = AttendanceCorrectionSource.Manual,
            Status = AttendanceCorrectionStatus.Pending,
            RequestedBy = requester,
            RequestedByName = actor.BoundedName,
            RequestedAt = DateTime.UtcNow,
            TenantId = db.EffectiveTenantId
        };
        db.AttendanceCorrections.Add(correction);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ConcurrencyExtensions.IsUniqueViolation(ex))
        {
            // Another request for the same day was saved between the check and this save (the filtered unique index).
            db.ChangeTracker.Clear();
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.PendingExists);
        }

        return AttendanceCorrectionResult.Done(correction);
    }

    // ---------------------------------------------------------------- the decision

    public async Task<AttendanceCorrectionResult> ApproveAsync(AttendanceActor actor, Guid correctionId, string? note, CancellationToken ct = default)
    {
        var (result, changedMeanwhile) = await ApproveOnceAsync(actor, correctionId, note, ct);

        // The summary's version changed under the approval (xmin): nothing was written. Each further attempt runs in a unit of work
        // of its own (a new scope: a new context and a new bus outbox), which reads what is stored now. The context of the attempt
        // that lost is not reused: it still holds what that attempt staged.
        for (var attempt = 2; changedMeanwhile && scopes is not null && attempt <= ApprovalAttempts; attempt++)
        {
            await using var scope = scopes.CreateAsyncScope();
            var again = scope.ServiceProvider.GetRequiredService<AttendanceCorrectionService>();
            (result, changedMeanwhile) = await again.ApproveOnceAsync(actor, correctionId, note, ct);
        }

        return changedMeanwhile
            ? AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.ChangedMeanwhile)
            : result!;
    }

    /// <summary>One attempt of an approval, in this unit of work. <c>ChangedMeanwhile</c>: the summary moved under it, and nothing was written.</summary>
    private async Task<(AttendanceCorrectionResult? Result, bool ChangedMeanwhile)> ApproveOnceAsync(
        AttendanceActor actor, Guid correctionId, string? note, CancellationToken ct)
    {
        var (correction, refusal) = await LoadForDecisionAsync(actor, correctionId, ct);
        if (refusal is not null)
            return (refusal, false);

        var summary = await db.AttendanceEmployeeSummaries
            .Include(s => s.Period)
            .FirstOrDefaultAsync(s => s.AttendancePeriodId == correction!.AttendancePeriodId && s.EmployeeId == correction.EmployeeId, ct);
        if (summary?.Period is null)
            return (AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.DayNotLocked), false);

        // The day may have changed since the request (a leave decision is the usual cause): the correction records what it really
        // replaces, and one that would change nothing is not approved.
        var day = await db.DailyAttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == correction!.EmployeeId && r.AttendanceDate == correction.AttendanceDate)
            .Select(r => new { r.Status, r.Source })
            .FirstOrDefaultAsync(ct);
        var current = day?.Status;
        if (current == correction!.NewStatus)
            return (AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.NoLongerApplies), false);
        // A leave took the day while the request waited: the day is the leave's now, as it would be for a new request.
        if (correction.Source == AttendanceCorrectionSource.Manual && day?.Source == AttendanceRecordSource.Leave)
            return (AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.LeaveMadeDay), false);
        correction.OldStatus = current;

        var now = DateTime.UtcNow;
        await ApplyCorrectionsAndStageSummaryAsync([correction], summary, summary.Period,
            correction.Source == AttendanceCorrectionSource.LeaveEvent ? AttendanceSummaryReason.LeaveEvent : AttendanceSummaryReason.Correction,
            now, ct);
        correction.Status = AttendanceCorrectionStatus.Approved;
        correction.DecidedBy = actor.UserId;
        correction.DecidedByName = actor.BoundedName;
        correction.DecidedAt = now;
        correction.DecisionNote = Clean(note);

        try
        {
            await db.SaveChangesAsync(ct);
            return (AttendanceCorrectionResult.Done(correction), false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return (null, true);
        }
    }

    public async Task<AttendanceCorrectionResult> RejectAsync(AttendanceActor actor, Guid correctionId, string? note, CancellationToken ct = default)
    {
        var (correction, refusal) = await LoadForDecisionAsync(actor, correctionId, ct);
        if (refusal is not null)
            return refusal;
        if (string.IsNullOrWhiteSpace(note))
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.RejectionNeedsNote);

        correction!.Status = AttendanceCorrectionStatus.Rejected;
        correction.DecidedBy = actor.UserId;
        correction.DecidedByName = actor.BoundedName;
        correction.DecidedAt = DateTime.UtcNow;
        correction.DecisionNote = Clean(note);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another decision on this request was saved first (the request's row version): this one wrote nothing.
            db.ChangeTracker.Clear();
            return AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.AlreadyDecided);
        }

        return AttendanceCorrectionResult.Done(correction);
    }

    /// <summary>The correction as a decision needs it, and the refusals an approval and a rejection share.</summary>
    private async Task<(AttendanceCorrection? Correction, AttendanceCorrectionResult? Refusal)> LoadForDecisionAsync(
        AttendanceActor actor, Guid correctionId, CancellationToken ct)
    {
        var correction = await db.AttendanceCorrections.FirstOrDefaultAsync(c => c.Id == correctionId, ct);
        if (correction is null)
            return (null, AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.NotFound, AttendanceCorrectionMessages.NotFound));
        if (await writer.RefuseOutOfScopeAsync(actor, correction.EmployeeId, ct) is { } refused)
        {
            return (null, AttendanceCorrectionResult.Refused(
                refused.Refusal == AttendanceRefusal.NotFound ? AttendanceCorrectionOutcome.NotFound : AttendanceCorrectionOutcome.OutOfScope,
                refused.Error!));
        }

        if (correction.Status != AttendanceCorrectionStatus.Pending)
            return (null, AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Conflict, AttendanceCorrectionMessages.AlreadyDecided));
        if (actor.UserId is not Guid decider)
            return (null, AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.Invalid, AttendanceCorrectionMessages.NoDecider));
        // Rule 4: the server compares the caller with the requester, whatever the caller's roles.
        if (correction.RequestedBy == decider)
            return (null, AttendanceCorrectionResult.Refused(AttendanceCorrectionOutcome.SeparationOfDuties, AttendanceCorrectionMessages.OwnRequest));
        return (correction, null);
    }

    // ---------------------------------------------------------------- a leave decision on locked days

    /// <summary>
    /// Makes the system corrections of a leave decision's locked days (D-LVA-12). <b>It does not save</b>: the leave consumer's one
    /// save writes them with the employee's status history and the unlocked days. The same event delivered again makes nothing
    /// (each correction carries the leave's reference; a filtered unique index holds one per employee, day and reference).
    /// </summary>
    [StagesOutboxMessages]
    public async Task<int> ApplySystemCorrectionAsync(
        Guid employeeId, Guid leaveRequestId, bool cancelled, IReadOnlyList<AttendanceLockedLeaveDay> lockedDays, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var made = 0;

        if (cancelled)
        {
            // A correction of the approval that still waits (the Pending mode) has nothing left to apply.
            var approvedReference = ApprovedReference(leaveRequestId);
            var waiting = await db.AttendanceCorrections
                .Where(c => c.EmployeeId == employeeId && c.SourceReference == approvedReference && c.Status == AttendanceCorrectionStatus.Pending)
                .ToListAsync(ct);
            foreach (var correction in waiting)
            {
                correction.Status = AttendanceCorrectionStatus.Rejected;
                correction.DecidedAt = now;
                correction.DecisionNote = AttendanceCorrectionMessages.LeaveCancelledBeforeApproval;
                made++;
            }
        }

        if (lockedDays.Count == 0)
            return made;

        var reference = cancelled ? CancelledReference(leaveRequestId) : ApprovedReference(leaveRequestId);
        var reason = cancelled ? AttendanceCorrectionMessages.LeaveCancelled(leaveRequestId) : AttendanceCorrectionMessages.LeaveApproved(leaveRequestId);
        var from = lockedDays.Min(d => d.Date);
        var to = lockedDays.Max(d => d.Date);

        var summaries = await db.AttendanceEmployeeSummaries
            .Include(s => s.Period)
            .Where(s => s.EmployeeId == employeeId && s.Period!.PeriodStart <= to && s.Period.PeriodEnd >= from)
            .ToListAsync(ct);
        var already = (await db.AttendanceCorrections.AsNoTracking()
                .Where(c => c.EmployeeId == employeeId && c.SourceReference == reference && c.AttendanceDate >= from && c.AttendanceDate <= to)
                .Select(c => c.AttendanceDate)
                .ToListAsync(ct))
            .ToHashSet();
        var autoApply = _options.Corrections.LeaveEventsAfterClose == AttendanceLeaveEventMode.AutoApply;

        foreach (var summary in summaries)
        {
            var period = summary.Period!;
            var days = lockedDays
                .Where(d => d.Date >= period.PeriodStart && d.Date <= period.PeriodEnd && !already.Contains(d.Date))
                .OrderBy(d => d.Date)
                .ToList();
            if (days.Count == 0)
                continue;

            var corrections = days.Select(day => new AttendanceCorrection
            {
                AttendancePeriodId = period.Id,
                EmployeeId = employeeId,
                AttendanceDate = day.Date,
                OldStatus = day.CurrentStatus,
                NewStatus = day.TargetStatus,
                Reason = reason,
                Source = AttendanceCorrectionSource.LeaveEvent,
                SourceReference = reference,
                Status = autoApply ? AttendanceCorrectionStatus.Approved : AttendanceCorrectionStatus.Pending,
                RequestedBy = null,
                RequestedAt = now,
                DecidedBy = null,
                DecidedAt = autoApply ? now : null,
                TenantId = summary.TenantId
            }).ToList();

            if (!autoApply)
            {
                // One request waits per employee and day: a person's request for the same day gives way to the leave decision.
                var dates = days.Select(d => d.Date).ToList();
                var standing = await db.AttendanceCorrections
                    .Where(c => c.EmployeeId == employeeId && dates.Contains(c.AttendanceDate) && c.Status == AttendanceCorrectionStatus.Pending)
                    .ToListAsync(ct);
                foreach (var earlier in standing)
                {
                    earlier.Status = AttendanceCorrectionStatus.Rejected;
                    earlier.DecidedAt = now;
                    earlier.DecisionNote = AttendanceCorrectionMessages.SupersededByLeave;
                }
            }

            db.AttendanceCorrections.AddRange(corrections);
            made += corrections.Count;
            if (autoApply)
                await ApplyCorrectionsAndStageSummaryAsync(corrections, summary, period, AttendanceSummaryReason.LeaveEvent, now, ct);
        }

        return made;
    }

    // ---------------------------------------------------------------- the one application

    /// <summary>
    /// Applies approved corrections of one employee and period: each day through the writer, the summary recomputed by the calculator
    /// from the stored days with these changes laid over them, the version raised by one, and that version staged for payroll. It does
    /// not save.
    /// </summary>
    [StagesOutboxMessages]
    private async Task ApplyCorrectionsAndStageSummaryAsync(
        IReadOnlyList<AttendanceCorrection> corrections,
        AttendanceEmployeeSummary summary,
        AttendancePeriod period,
        AttendanceSummaryReason reason,
        DateTime now,
        CancellationToken ct)
    {
        var employeeId = summary.EmployeeId;
        var days = await db.DailyAttendanceRecords.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate >= period.PeriodStart && r.AttendanceDate <= period.PeriodEnd)
            .ToDictionaryAsync(r => r.AttendanceDate, r => r.Status, ct);

        foreach (var correction in corrections)
        {
            await writer.ApplyApprovedCorrectionAsync(correction, ct);
            if (correction.NewStatus is { } status)
                days[correction.AttendanceDate] = status;
            else
                days.Remove(correction.AttendanceDate);
        }

        var employee = await eligibility.GetOneAsync(employeeId, ct);
        var figures = AttendanceSummaryCalculator.Calculate(
            days, period.PeriodStart, period.PeriodEnd, calendar, employee?.HireDate, employee?.EndOfService, _options.Close.UnrecordedWorkingDays);

        summary.Version += 1;
        summary.WorkingDays = figures.WorkingDays;
        summary.PresentDays = figures.PresentDays;
        summary.AbsentDays = figures.AbsentDays;
        summary.OnLeaveDays = figures.OnLeaveDays;
        summary.RemoteDays = figures.RemoteDays;
        summary.UnrecordedWorkingDays = figures.UnrecordedWorkingDays;
        summary.LastReason = reason;
        summary.LastCorrectionId = corrections[^1].Id;
        summary.PublishedAt = now;
        foreach (var correction in corrections)
            correction.ResultingVersion = summary.Version;

        await periods.PublishSummaryAsync(summary, period, corrections[^1].Id, now, CorrelationIdFlow.Get(), ct);
    }

    private async Task<AttendanceEmployeeSummary?> SummaryCoveringAsync(Guid employeeId, DateOnly date, bool tracked, CancellationToken ct)
    {
        var query = db.AttendanceEmployeeSummaries.Include(s => s.Period)
            .Where(s => s.EmployeeId == employeeId && s.Period!.PeriodStart <= date && s.Period.PeriodEnd >= date);
        return await (tracked ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private static string? Clean(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();
}
