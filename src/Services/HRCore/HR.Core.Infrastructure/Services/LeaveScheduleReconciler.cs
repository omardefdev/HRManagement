using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HRManagement.SharedKernel.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services;

/// <summary>
/// Moves employees into and out of <see cref="EmploymentStatus.OnLeave"/> as their approved leave
/// windows open and close.
/// <para>
/// Approving leave used to flip the employee to OnLeave immediately, with no path back: once the
/// leave ended nothing returned them to Active, so the workforce slowly filled with employees
/// permanently marked on leave. Attendance rows already carry the authoritative leave window, so
/// they are the source of truth here.
/// </para>
/// </summary>
public sealed class LeaveScheduleReconciler(HRCoreDbContext db, ILogger<LeaveScheduleReconciler> logger)
{
    public sealed record ReconcileResult(int MovedToOnLeave, int ReturnedToActive);

    public async Task<ReconcileResult> ReconcileAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var onLeaveToday = await db.DailyAttendanceRecords
            .AsNoTracking()
            .Where(a => a.AttendanceDate == asOf && a.Status == DailyAttendanceStatus.OnLeave)
            .Select(a => a.EmployeeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var onLeaveSet = onLeaveToday.ToHashSet();

        var movedToOnLeave = 0;
        if (onLeaveSet.Count > 0)
        {
            var toOpen = await db.Employees
                .Where(e => onLeaveSet.Contains(e.Id) && e.CurrentStatus == EmploymentStatus.Active)
                .ToListAsync(cancellationToken);

            foreach (var employee in toOpen)
            {
                Transition(employee, EmploymentStatus.OnLeave, asOf, "بدء إجازة معتمدة");
                movedToOnLeave++;
            }
        }

        // Anyone marked OnLeave whose window no longer covers today goes back to Active. Only
        // OnLeave is reversed here: terminal statuses (separated, suspended) are not ours to undo.
        var toClose = await db.Employees
            .Where(e => e.CurrentStatus == EmploymentStatus.OnLeave && !onLeaveSet.Contains(e.Id))
            .ToListAsync(cancellationToken);

        foreach (var employee in toClose)
            Transition(employee, EmploymentStatus.Active, asOf, "انتهاء إجازة معتمدة");

        if (movedToOnLeave > 0 || toClose.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Leave schedule reconciled for {AsOf}: {Opened} moved to OnLeave, {Closed} returned to Active.",
                asOf, movedToOnLeave, toClose.Count);
        }

        return new ReconcileResult(movedToOnLeave, toClose.Count);
    }

    private void Transition(Employee employee, EmploymentStatus target, DateOnly asOf, string reason)
    {
        var previous = employee.CurrentStatus;
        employee.CurrentStatus = target;

        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = employee.Id,
            OldStatus = previous.ToString(),
            NewStatus = target.ToString(),
            ChangedAt = asOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Reason = $"LeaveScheduleReconciler: {reason}"
        });
    }
}
