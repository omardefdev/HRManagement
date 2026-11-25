using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using HRManagement.ServiceDefaults.Messaging;

namespace HR.Core.Infrastructure.Integration;

/// <summary>
/// PLAN-57 (§2): what a leave decision does to the attendance days. Both methods go through <see cref="AttendanceDayWriter"/> and
/// <b>neither saves</b>: the leave consumer writes the employee's status history, the days and any system correction in its one save.
/// The old close (<c>ClosePeriodAndPublishAsync</c>) stays until <c>AttendancePeriodService</c> replaces it (PLAN-57 P3).
/// </summary>
public class AttendanceIntegrationService(HRCoreDbContext db, IPublishEndpoint publish, AttendanceDayWriter writer)
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

    public async Task<int> ClosePeriodAndPublishAsync(
        DateOnly periodStart,
        DateOnly periodEnd,
        Guid? orgUnitId,
        CancellationToken ct = default)
    {
        var employeeQuery = db.Employees.AsNoTracking().Where(e => e.CurrentStatus != EmploymentStatus.Terminated);
        if (orgUnitId.HasValue)
            employeeQuery = employeeQuery.Where(e => e.PrimaryOrgUnitId == orgUnitId);

        var employees = await employeeQuery.Select(e => e.Id).ToListAsync(ct);
        var published = 0;

        foreach (var employeeId in employees)
        {
            var records = await db.DailyAttendanceRecords
                .Where(r => r.EmployeeId == employeeId
                            && r.AttendanceDate >= periodStart
                            && r.AttendanceDate <= periodEnd)
                .ToListAsync(ct);

            var summary = new
            {
                PresentDays = records.Count(r => r.Status == DailyAttendanceStatus.Present),
                AbsentDays = records.Count(r => r.Status == DailyAttendanceStatus.Absent),
                OnLeaveDays = records.Count(r => r.Status == DailyAttendanceStatus.OnLeave),
                RemoteDays = records.Count(r => r.Status == DailyAttendanceStatus.Remote)
            };

            await publish.Publish<IAttendancePeriodClosed>(new
            {
                TransactionId = Guid.NewGuid(),
                EmployeeId = employeeId,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                summary.PresentDays,
                summary.AbsentDays,
                summary.OnLeaveDays,
                summary.RemoteDays,
                OccurredAt = DateTime.UtcNow,
                CorrelationId = (string?)null,
                SourceSystem = "HR.Core"
            }, ct);

            published++;
        }

        await db.SaveChangesAsync(ct);
        return published;
    }
}
