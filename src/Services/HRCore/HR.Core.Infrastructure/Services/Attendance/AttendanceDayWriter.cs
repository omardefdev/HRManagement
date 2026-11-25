using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Attendance;

/// <summary>Who writes: the caller's subject and his org scope. A consumer (a leave decision) acts as <see cref="System"/>.</summary>
public readonly record struct AttendanceActor(Guid? UserId, OrgAccessScope Scope)
{
    public static AttendanceActor System { get; } = new(null, OrgAccessScope.Unrestricted());
}

public enum AttendanceRefusal
{
    None = 0,
    /// <summary>The record, the employee or the unit does not exist in the caller's agency.</summary>
    NotFound,
    /// <summary>The employee or the unit is outside the caller's org scope.</summary>
    OutOfScope,
    /// <summary>The day is in a closed attendance period of the employee.</summary>
    Locked,
    /// <summary>The employee already has a record on that day.</summary>
    Duplicate,
    /// <summary>A rule of the write is broken (a status typed by hand that only a leave gives, a leave-made record edited by hand).</summary>
    Invalid
}

public sealed record AttendanceWriteResult(AttendanceRefusal Refusal, string? Error, DailyAttendanceRecord? Record)
{
    public bool Ok => Refusal == AttendanceRefusal.None;

    public static AttendanceWriteResult Done(DailyAttendanceRecord? record) => new(AttendanceRefusal.None, null, record);

    public static AttendanceWriteResult Refused(AttendanceRefusal refusal, string error) => new(refusal, error, null);
}

/// <summary>What a bulk entry did: the employees it covered, the days it wrote, and the days it skipped and why.</summary>
public sealed record AttendanceBulkResult(
    AttendanceRefusal Refusal, string? Error, int Employees, int Written, int SkippedLocked, int SkippedExisting)
{
    public bool Ok => Refusal == AttendanceRefusal.None;
}

/// <summary>A locked day a leave decision touches: the writer leaves it, and the correction service makes a system correction of it.</summary>
public sealed record AttendanceLockedLeaveDay(DateOnly Date, DailyAttendanceStatus? CurrentStatus, DailyAttendanceStatus? TargetStatus);

public sealed record AttendanceLeaveResult(int Written, IReadOnlyList<AttendanceLockedLeaveDay> LockedDays);

public static class AttendanceMessages
{
    public const string LockedDay = "اليوم ضمن فترة حضور مغلقة؛ قدّم طلب تصحيح";
    public const string OnLeaveByHand = "حالة «في إجازة» تأتي من إجازة معتمدة فقط، ولا تُدخل يدوياً ولا جماعياً.";
    public const string LeaveMadeRecord = "هذا السجل أنشأته إجازة معتمدة؛ لا يُعدَّل ولا يُحذف يدوياً. يتغير بإلغاء الإجازة.";
    public const string DuplicateDay = "يوجد سجل حضور لهذا الموظف في هذا اليوم. عدّل السجل الموجود بدل إنشاء سجل جديد.";
    public const string EmployeeNotFound = "الموظف غير موجود.";
    public const string RecordNotFound = "سجل الحضور غير موجود.";
    public const string UnitNotFound = "الوحدة التنظيمية غير موجودة.";
    public const string EmployeeOutOfScope = "الموظف خارج نطاق صلاحيتك.";
    public const string UnitOutOfScope = "الوحدة التنظيمية خارج نطاق صلاحيتك.";
    public const string UnknownStatus = "حالة الحضور غير معروفة.";
}

/// <summary>
/// PLAN-57 (§2): the one writer of an attendance day. Every create, change and removal of a <see cref="DailyAttendanceRecord"/> in
/// HR.Core goes through it (<c>AttendanceSingleWriterArchitectureTests</c>). <b>It never saves</b>: its caller saves once, so the day,
/// its audit rows and whatever else the caller changes are written together.
/// <list type="number">
/// <item>A manual or bulk write on a locked day is refused, and nothing changes.</item>
/// <item>The employee and the date never change after creation.</item>
/// <item>«في إجازة» is never written by hand or in bulk; it comes from <see cref="ApplyLeaveAsync"/> with the leave's reference, and
/// a leave-made record is never edited or deleted by hand.</item>
/// <item>The bulk entry writes working days only, nothing after today, for the unit's tree, and skips locked and existing days.</item>
/// <item>A duplicate day is a validation message.</item>
/// <item>A leave keeps the status it replaces, so that its cancellation restores it; on a locked day it changes nothing here and
/// reports the day for a system correction.</item>
/// <item>The scope of the employee (PLAN-52) is checked here, once, for a create, a change, a removal, the bulk entry and a correction.</item>
/// </list>
/// </summary>
public sealed class AttendanceDayWriter(
    HRCoreDbContext db,
    AttendanceLockService locks,
    AttendanceCalendar calendar,
    AttendanceEligibility eligibility)
{
    /// <summary>The reference a leave-made day carries.</summary>
    public static string LeaveReference(Guid leaveRequestId) => $"LeaveRequestId={leaveRequestId:D}";

    /// <summary>The reference a corrected day carries.</summary>
    public static string CorrectionReference(Guid correctionId) => $"AttendanceCorrection:{correctionId:D}";

    /// <summary>PLAN-52's check of the employee's unit against the caller's scope, in one place.</summary>
    public async Task<AttendanceWriteResult?> RefuseOutOfScopeAsync(AttendanceActor actor, Guid employeeId, CancellationToken ct = default)
    {
        var employee = await db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => new { e.Id, e.PrimaryOrgUnitId })
            .FirstOrDefaultAsync(ct);
        if (employee is null)
            return AttendanceWriteResult.Refused(AttendanceRefusal.NotFound, AttendanceMessages.EmployeeNotFound);
        if (!actor.Scope.CanAccess(employee.PrimaryOrgUnitId))
            return AttendanceWriteResult.Refused(AttendanceRefusal.OutOfScope, AttendanceMessages.EmployeeOutOfScope);
        return null;
    }

    public async Task<AttendanceWriteResult> CreateManualAsync(
        AttendanceActor actor, Guid employeeId, DateOnly date, DailyAttendanceStatus status, string? notes, CancellationToken ct = default)
    {
        if (await RefuseOutOfScopeAsync(actor, employeeId, ct) is { } refused)
            return refused;
        if (RefuseManualStatus(status) is { } badStatus)
            return badStatus;
        if (await locks.IsLockedAsync(employeeId, date, ct))
            return AttendanceWriteResult.Refused(AttendanceRefusal.Locked, AttendanceMessages.LockedDay);
        if (await ExistsAsync(employeeId, date, ct))
            return AttendanceWriteResult.Refused(AttendanceRefusal.Duplicate, AttendanceMessages.DuplicateDay);

        var record = new DailyAttendanceRecord
        {
            EmployeeId = employeeId,
            AttendanceDate = date,
            Status = status,
            Notes = Clean(notes),
            Source = AttendanceRecordSource.Manual
        };
        db.DailyAttendanceRecords.Add(record);
        return AttendanceWriteResult.Done(record);
    }

    /// <summary>Changes the status and the note of a record. Its employee and its date are not parameters: they never change.</summary>
    public async Task<AttendanceWriteResult> UpdateManualAsync(
        AttendanceActor actor, Guid recordId, DailyAttendanceStatus status, string? notes, CancellationToken ct = default)
    {
        var record = await db.DailyAttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return AttendanceWriteResult.Refused(AttendanceRefusal.NotFound, AttendanceMessages.RecordNotFound);
        if (await RefuseOutOfScopeAsync(actor, record.EmployeeId, ct) is { } refused)
            return refused;
        if (record.Source == AttendanceRecordSource.Leave)
            return AttendanceWriteResult.Refused(AttendanceRefusal.Invalid, AttendanceMessages.LeaveMadeRecord);
        if (RefuseManualStatus(status) is { } badStatus)
            return badStatus;
        if (await locks.IsLockedAsync(record.EmployeeId, record.AttendanceDate, ct))
            return AttendanceWriteResult.Refused(AttendanceRefusal.Locked, AttendanceMessages.LockedDay);

        record.Status = status;
        record.Notes = Clean(notes);
        record.Source = AttendanceRecordSource.Manual;
        record.SourceReference = null;
        record.ReplacedStatus = null;
        return AttendanceWriteResult.Done(record);
    }

    public async Task<AttendanceWriteResult> DeleteManualAsync(AttendanceActor actor, Guid recordId, CancellationToken ct = default)
    {
        var record = await db.DailyAttendanceRecords.FirstOrDefaultAsync(r => r.Id == recordId, ct);
        if (record is null)
            return AttendanceWriteResult.Refused(AttendanceRefusal.NotFound, AttendanceMessages.RecordNotFound);
        if (await RefuseOutOfScopeAsync(actor, record.EmployeeId, ct) is { } refused)
            return refused;
        if (record.Source == AttendanceRecordSource.Leave)
            return AttendanceWriteResult.Refused(AttendanceRefusal.Invalid, AttendanceMessages.LeaveMadeRecord);
        if (await locks.IsLockedAsync(record.EmployeeId, record.AttendanceDate, ct))
            return AttendanceWriteResult.Refused(AttendanceRefusal.Locked, AttendanceMessages.LockedDay);

        db.DailyAttendanceRecords.Remove(record);
        return AttendanceWriteResult.Done(record);
    }

    /// <summary>
    /// Fills a month for the employees of a unit's tree: each employee's working days up to today (never a rest day, a configured
    /// holiday, a future day, a day before his hire or after his end of service), skipping locked days and days that have a record.
    /// </summary>
    public async Task<AttendanceBulkResult> BulkFillAsync(
        AttendanceActor actor, Guid orgUnitId, DateOnly monthStart, DailyAttendanceStatus status, CancellationToken ct = default)
    {
        if (RefuseManualStatus(status) is { } badStatus)
            return new AttendanceBulkResult(badStatus.Refusal, badStatus.Error, 0, 0, 0, 0);
        if (await eligibility.UnitTreePathAsync(orgUnitId, ct) is null)
            return new AttendanceBulkResult(AttendanceRefusal.NotFound, AttendanceMessages.UnitNotFound, 0, 0, 0, 0);
        if (!actor.Scope.CanAccess(orgUnitId))
            return new AttendanceBulkResult(AttendanceRefusal.OutOfScope, AttendanceMessages.UnitOutOfScope, 0, 0, 0, 0);

        var start = new DateOnly(monthStart.Year, monthStart.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var today = calendar.Today;
        var lastDay = end <= today ? end : today;

        var employees = await eligibility.GetAsync(orgUnitId, start, end, ct);
        if (employees.Count == 0 || lastDay < start)
            return new AttendanceBulkResult(AttendanceRefusal.None, null, employees.Count, 0, 0, 0);

        var ids = employees.Select(e => e.Id).ToList();
        var lockedDays = await locks.GetLockedDaysAsync(ids, start, lastDay, ct);
        var existing = (await db.DailyAttendanceRecords.AsNoTracking()
                .Where(r => ids.Contains(r.EmployeeId) && r.AttendanceDate >= start && r.AttendanceDate <= lastDay)
                .Select(r => new { r.EmployeeId, r.AttendanceDate })
                .ToListAsync(ct))
            .Select(r => (r.EmployeeId, r.AttendanceDate))
            .ToHashSet();

        int written = 0, skippedLocked = 0, skippedExisting = 0;
        foreach (var employee in employees)
        {
            foreach (var day in AttendanceSummaryCalculator.WorkingDays(start, lastDay, calendar, employee.HireDate, employee.EndOfService))
            {
                if (lockedDays.IsLocked(employee.Id, day))
                {
                    skippedLocked++;
                    continue;
                }

                if (existing.Contains((employee.Id, day)))
                {
                    skippedExisting++;
                    continue;
                }

                db.DailyAttendanceRecords.Add(new DailyAttendanceRecord
                {
                    EmployeeId = employee.Id,
                    AttendanceDate = day,
                    Status = status,
                    Source = AttendanceRecordSource.Bulk
                });
                written++;
            }
        }

        return new AttendanceBulkResult(AttendanceRefusal.None, null, employees.Count, written, skippedLocked, skippedExisting);
    }

    /// <summary>
    /// An approved leave takes its days: an unlocked day becomes «في إجازة» with the leave's reference, and the status it had is
    /// kept in <c>ReplacedStatus</c>. A day another leave holds is left to that leave. A locked day is not written: it is returned,
    /// and the caller makes a system correction of it (D-LVA-12).
    /// </summary>
    public async Task<AttendanceLeaveResult> ApplyLeaveAsync(
        Guid employeeId, DateOnly start, DateOnly end, Guid leaveRequestId, string? notes, CancellationToken ct = default)
    {
        if (end < start)
            (start, end) = (end, start);

        var reference = LeaveReference(leaveRequestId);
        var records = await db.DailyAttendanceRecords
            .Where(r => r.EmployeeId == employeeId && r.AttendanceDate >= start && r.AttendanceDate <= end)
            .ToDictionaryAsync(r => r.AttendanceDate, ct);
        var lockedDays = await locks.GetLockedDaysAsync([employeeId], start, end, ct);

        var written = 0;
        var locked = new List<AttendanceLockedLeaveDay>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            records.TryGetValue(day, out var record);
            if (record is { Source: AttendanceRecordSource.Leave })
                continue;

            if (lockedDays.IsLocked(employeeId, day))
            {
                if (record?.Status != DailyAttendanceStatus.OnLeave)
                    locked.Add(new AttendanceLockedLeaveDay(day, record?.Status, DailyAttendanceStatus.OnLeave));
                continue;
            }

            if (record is null)
            {
                db.DailyAttendanceRecords.Add(new DailyAttendanceRecord
                {
                    EmployeeId = employeeId,
                    AttendanceDate = day,
                    Status = DailyAttendanceStatus.OnLeave,
                    Notes = notes,
                    Source = AttendanceRecordSource.Leave,
                    SourceReference = reference,
                    ReplacedStatus = null
                });
            }
            else
            {
                record.ReplacedStatus = record.Status;
                record.Status = DailyAttendanceStatus.OnLeave;
                record.Notes = notes ?? record.Notes;
                record.Source = AttendanceRecordSource.Leave;
                record.SourceReference = reference;
            }

            written++;
        }

        return new AttendanceLeaveResult(written, locked);
    }

    /// <summary>
    /// A cancelled leave gives its days back: only the records that carry this leave's reference are touched. A record that
    /// replaced a status takes it back; a record the leave created is removed. A locked day is returned for a system correction.
    /// </summary>
    public async Task<AttendanceLeaveResult> RevertLeaveAsync(
        Guid employeeId, DateOnly start, DateOnly end, Guid leaveRequestId, CancellationToken ct = default)
    {
        if (end < start)
            (start, end) = (end, start);

        var reference = LeaveReference(leaveRequestId);
        var records = await db.DailyAttendanceRecords
            .Where(r => r.EmployeeId == employeeId
                        && r.AttendanceDate >= start
                        && r.AttendanceDate <= end
                        && r.Source == AttendanceRecordSource.Leave
                        // A day a leave made before PLAN-57 carries no reference (the migration marked it by its note): the leave
                        // that is cancelled over it takes it, as the old removal did. A manual record is never touched.
                        && (r.SourceReference == reference || r.SourceReference == null))
            .OrderBy(r => r.AttendanceDate)
            .ToListAsync(ct);
        if (records.Count == 0)
            return new AttendanceLeaveResult(0, []);

        var lockedDays = await locks.GetLockedDaysAsync([employeeId], start, end, ct);
        var written = 0;
        var locked = new List<AttendanceLockedLeaveDay>();
        foreach (var record in records)
        {
            if (lockedDays.IsLocked(employeeId, record.AttendanceDate))
            {
                locked.Add(new AttendanceLockedLeaveDay(record.AttendanceDate, record.Status, record.ReplacedStatus));
                continue;
            }

            if (record.ReplacedStatus is { } earlier)
            {
                record.Status = earlier;
                record.ReplacedStatus = null;
                record.Source = AttendanceRecordSource.Manual;
                record.SourceReference = null;
                record.Notes = null;
            }
            else
            {
                db.DailyAttendanceRecords.Remove(record);
            }

            written++;
        }

        return new AttendanceLeaveResult(written, locked);
    }

    /// <summary>
    /// The only write of a locked day (§1, rule 9): an approved correction. The day takes the correction's status (or loses its
    /// record when the correction undoes a leave that created it). A leave's correction leaves a leave-made record, so that the
    /// leave's cancellation finds it; any other correction leaves a corrected record.
    /// </summary>
    public async Task<DailyAttendanceRecord?> ApplyApprovedCorrectionAsync(AttendanceCorrection correction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(correction);
        var record = db.DailyAttendanceRecords.Local
                         .FirstOrDefault(r => r.EmployeeId == correction.EmployeeId && r.AttendanceDate == correction.AttendanceDate)
                     ?? await db.DailyAttendanceRecords
                         .FirstOrDefaultAsync(r => r.EmployeeId == correction.EmployeeId && r.AttendanceDate == correction.AttendanceDate, ct);

        if (correction.NewStatus is not { } target)
        {
            if (record is not null)
                db.DailyAttendanceRecords.Remove(record);
            return null;
        }

        var byLeave = correction.Source == AttendanceCorrectionSource.LeaveEvent && target == DailyAttendanceStatus.OnLeave;
        var source = byLeave ? AttendanceRecordSource.Leave : AttendanceRecordSource.Correction;
        var reference = byLeave ? LeaveReferenceOf(correction) : CorrectionReference(correction.Id);

        if (record is null)
        {
            record = new DailyAttendanceRecord
            {
                EmployeeId = correction.EmployeeId,
                AttendanceDate = correction.AttendanceDate,
                Status = target,
                Source = source,
                SourceReference = reference,
                ReplacedStatus = null
            };
            db.DailyAttendanceRecords.Add(record);
            return record;
        }

        record.ReplacedStatus = byLeave ? record.Status : null;
        record.Status = target;
        record.Source = source;
        record.SourceReference = reference;
        return record;
    }

    /// <summary>The leave reference inside a leave correction's own reference (<c>LeaveRequestId=…:Approved</c>).</summary>
    private static string? LeaveReferenceOf(AttendanceCorrection correction)
    {
        var reference = correction.SourceReference;
        if (string.IsNullOrEmpty(reference))
            return null;
        var cut = reference.LastIndexOf(':');
        return cut > 0 ? reference[..cut] : reference;
    }

    private static AttendanceWriteResult? RefuseManualStatus(DailyAttendanceStatus status)
    {
        if (!Enum.IsDefined(status))
            return AttendanceWriteResult.Refused(AttendanceRefusal.Invalid, AttendanceMessages.UnknownStatus);
        if (status == DailyAttendanceStatus.OnLeave)
            return AttendanceWriteResult.Refused(AttendanceRefusal.Invalid, AttendanceMessages.OnLeaveByHand);
        return null;
    }

    private async Task<bool> ExistsAsync(Guid employeeId, DateOnly date, CancellationToken ct) =>
        db.DailyAttendanceRecords.Local.Any(r => r.EmployeeId == employeeId && r.AttendanceDate == date
                                                 && db.Entry(r).State != EntityState.Deleted)
        || await db.DailyAttendanceRecords.AsNoTracking().AnyAsync(r => r.EmployeeId == employeeId && r.AttendanceDate == date, ct);

    private static string? Clean(string? notes) => string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
}
