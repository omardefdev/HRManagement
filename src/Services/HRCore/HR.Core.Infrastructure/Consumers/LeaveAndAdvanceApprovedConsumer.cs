using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Consumers;

public class LeaveApprovedConsumer(HRCoreDbContext db, AttendanceIntegrationService attendance) : IConsumer<ILeaveApproved>
{
    public async Task Consume(ConsumeContext<ILeaveApproved> context)
    {
        var m = context.Message;
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == m.EmployeeId);
        if (employee is null)
            return;

        var marker = $"LeaveApproved:{m.LeaveRequestId}";
        var exists = await db.EmploymentStatusHistories.AnyAsync(x => x.EmployeeId == m.EmployeeId && x.Reason != null && x.Reason.Contains(marker));
        if (exists)
            return;

        // Approving leave that starts next month must not put the employee on leave today.
        // The status flip belongs to the leave window, and LeaveScheduleReconciler moves the
        // employee in and out of OnLeave as the window opens and closes.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var leaveHasStarted = DateOnly.FromDateTime(m.StartDate) <= today
                              && today <= DateOnly.FromDateTime(m.EndDate);

        var oldStatus = employee.CurrentStatus;
        if (leaveHasStarted)
            employee.CurrentStatus = EmploymentStatus.OnLeave;

        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = m.EmployeeId,
            OldStatus = oldStatus.ToString(),
            NewStatus = (leaveHasStarted ? EmploymentStatus.OnLeave : oldStatus).ToString(),
            ChangedAt = m.OccurredAt,
            Reason = leaveHasStarted
                ? $"{marker} Source={m.SourceSystem}"
                : $"{marker} Source={m.SourceSystem} Scheduled={m.StartDate:yyyy-MM-dd}"
        });
        await db.SaveChangesAsync();

        await attendance.UpsertLeaveAttendanceAsync(
            m.EmployeeId,
            m.StartDate,
            m.EndDate,
            $"إجازة معتمدة: {m.LeaveType}",
            context.CancellationToken);
    }
}

public class LeaveCancelledConsumer(HRCoreDbContext db, AttendanceIntegrationService attendance) : IConsumer<ILeaveCancelled>
{
    public async Task Consume(ConsumeContext<ILeaveCancelled> context)
    {
        var m = context.Message;
        var employee = await db.Employees.FirstOrDefaultAsync(x => x.Id == m.EmployeeId);
        if (employee is null)
            return;

        var marker = $"LeaveCancelled:{m.LeaveRequestId}";
        var exists = await db.EmploymentStatusHistories.AnyAsync(x => x.EmployeeId == m.EmployeeId && x.Reason != null && x.Reason.Contains(marker));
        if (exists)
            return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var leaveStart = DateOnly.FromDateTime(m.StartDate);
        var leaveEnd = DateOnly.FromDateTime(m.EndDate);
        var leaveActiveNow = leaveStart <= today && today <= leaveEnd;

        var oldStatus = employee.CurrentStatus;
        if (leaveActiveNow && employee.CurrentStatus == EmploymentStatus.OnLeave)
            employee.CurrentStatus = EmploymentStatus.Active;

        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = m.EmployeeId,
            OldStatus = oldStatus.ToString(),
            NewStatus = employee.CurrentStatus.ToString(),
            ChangedAt = m.OccurredAt,
            Reason = $"{marker} Source={m.SourceSystem}"
        });
        await db.SaveChangesAsync();

        await attendance.RemoveLeaveAttendanceAsync(
            m.EmployeeId,
            m.StartDate,
            m.EndDate,
            context.CancellationToken);
    }
}

public class AdvanceApprovedConsumer(HRCoreDbContext db) : IConsumer<IAdvanceApproved>
{
    public async Task Consume(ConsumeContext<IAdvanceApproved> context)
    {
        var m = context.Message;
        var employeeExists = await db.Employees.AnyAsync(x => x.Id == m.EmployeeId);
        if (!employeeExists)
        {
            return;
        }

        var marker = $"AdvanceApproved:{m.AdvanceRequestId}";
        var exists = await db.EmploymentStatusHistories.AnyAsync(x => x.EmployeeId == m.EmployeeId && x.Reason != null && x.Reason.Contains(marker));
        if (exists)
        {
            return;
        }

        db.EmploymentStatusHistories.Add(new EmploymentStatusHistory
        {
            EmployeeId = m.EmployeeId,
            NewStatus = "Active",
            ChangedAt = m.OccurredAt,
            Reason = $"AdvanceApproved:{m.AdvanceRequestId} Amount={m.Amount} Source={m.SourceSystem}"
        });
        await db.SaveChangesAsync();
    }
}
