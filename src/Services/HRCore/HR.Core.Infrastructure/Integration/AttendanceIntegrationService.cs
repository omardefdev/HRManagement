using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.ServiceDefaults.Messaging;

namespace HR.Core.Infrastructure.Integration;

/// <summary>
/// PLAN-57 (§2): what a leave decision does to the attendance days. Both methods go through <see cref="AttendanceDayWriter"/> and
/// <b>neither saves</b>: the leave consumer writes the employee's status history, the days and any system correction in its one save.
/// The old close (<c>ClosePeriodAndPublishAsync</c>) is gone: a period is closed by <see cref="AttendancePeriodService"/>.
/// </summary>
public class AttendanceIntegrationService(AttendanceDayWriter writer)
{
    /// <summary>An approved leave takes its days (a day keeps the status it had, so that a cancellation restores it).</summary>
    public async Task<AttendanceLeaveResult> UpsertLeaveAttendanceAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        Guid leaveRequestId,
        string? notes,
        CancellationToken ct = default) =>
        await writer.ApplyLeaveAsync(
            employeeId, DateOnly.FromDateTime(startDate), DateOnly.FromDateTime(endDate), leaveRequestId, notes, ct);

    /// <summary>A cancelled leave gives back the days that carry its reference, and no other record.</summary>
    public async Task<AttendanceLeaveResult> RemoveLeaveAttendanceAsync(
        Guid employeeId,
        DateTime startDate,
        DateTime endDate,
        Guid leaveRequestId,
        CancellationToken ct = default) =>
        await writer.RevertLeaveAsync(
            employeeId, DateOnly.FromDateTime(startDate), DateOnly.FromDateTime(endDate), leaveRequestId, ct);
}
