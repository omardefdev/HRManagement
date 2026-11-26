using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.ServiceDefaults.Messaging;

namespace HR.Core.Infrastructure.Integration;

/// <summary>
/// PLAN-57 (§2, §3): what a leave decision does to the attendance days. An unlocked day is written by
/// <see cref="AttendanceDayWriter"/>; a locked day becomes a system correction (<see cref="AttendanceCorrectionService"/>, D-LVA-12),
/// which stages the summary's next version for payroll. <b>Neither method saves</b>: the leave consumer writes the employee's status
/// history, the days, the corrections and the staged event in its one save.
/// The old close (<c>ClosePeriodAndPublishAsync</c>) is gone: a period is closed by <see cref="AttendancePeriodService"/>.
/// </summary>
public class AttendanceIntegrationService(AttendanceDayWriter writer, AttendanceCorrectionService corrections)
{
    /// <summary>An approved leave takes its days (a day keeps the status it had, so that a cancellation restores it).</summary>
    [StagesOutboxMessages]
    public async Task<AttendanceLeaveResult> UpsertLeaveAttendanceAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        Guid leaveRequestId,
        string? notes,
        CancellationToken ct = default)
    {
        var result = await writer.ApplyLeaveAsync(
            employeeId, DateOnly.FromDateTime(startDate), DateOnly.FromDateTime(endDate), leaveRequestId, notes, ct);
        if (result.LockedDays.Count > 0)
            await corrections.ApplySystemCorrectionAsync(employeeId, leaveRequestId, cancelled: false, result.LockedDays, ct);
        return result;
    }

    /// <summary>A cancelled leave gives back the days that carry its reference, and no other record.</summary>
    [StagesOutboxMessages]
    public async Task<AttendanceLeaveResult> RemoveLeaveAttendanceAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        Guid leaveRequestId,
        CancellationToken ct = default)
    {
        var result = await writer.RevertLeaveAsync(
            employeeId, DateOnly.FromDateTime(startDate), DateOnly.FromDateTime(endDate), leaveRequestId, ct);
        // Always called for a cancellation: a correction of the approval that still waits for a person is withdrawn with it.
        await corrections.ApplySystemCorrectionAsync(employeeId, leaveRequestId, cancelled: true, result.LockedDays, ct);
        return result;
    }
}
