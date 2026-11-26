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
/// permanently marked on leave. The attendance days an approved leave made (<c>Source = Leave</c>) carry the authoritative
/// leave window, so they are the source of truth here; a day typed «في إجازة» by hand is not (PLAN-57).
/// </para>
/// </summary>
public sealed class LeaveScheduleReconciler(HRCoreDbContext db, ILogger<LeaveScheduleReconciler> logger)
{
    public sealed record ReconcileResult(int MovedToOnLeave, int ReturnedToActive);

    public async Task<ReconcileResult> ReconcileAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        var onLeaveToday = await db.DailyAttendanceRecords
            .AsNoTracking()
            // PLAN-57 (§2): only a day an approved leave made is a leave. A «في إجازة» typed by hand before this plan is not.
            .Where(a => a.AttendanceDate == asOf
                        && a.Status == DailyAttendanceStatus.OnLeave
                        && a.Source == AttendanceRecordSource.Leave)
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
        var candidates = await db.Employees
            .Where(e => e.CurrentStatus == EmploymentStatus.OnLeave && !onLeaveSet.Contains(e.Id))
            .ToListAsync(cancellationToken);

        // PLAN-57 (§2): only an employee a leave put on leave is brought back: his last move to OnLeave was written by the leave
        // consumer («LeaveApproved:») or by this reconciler. One whom a person put on leave by hand is not ours to return.
        var toClose = new List<Employee>();
        if (candidates.Count > 0)
        {
            var candidateIds = candidates.Select(e => e.Id).ToList();
            var onLeaveName = EmploymentStatus.OnLeave.ToString();
            var moves = await db.EmploymentStatusHistories.AsNoTracking()
                // A move: a row that changed the status. A row that notes «on leave» over «on leave» (another leave's cancellation,
                // a medical decision) is not what put the employee on leave, and must not hide the row that did.
                .Where(h => candidateIds.Contains(h.EmployeeId) && h.NewStatus == onLeaveName && h.OldStatus != onLeaveName)
                .Select(h => new { h.EmployeeId, h.ChangedAt, h.CreatedAt, h.Reason })
                .ToListAsync(cancellationToken);
            var lastReason = moves
                .GroupBy(h => h.EmployeeId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.CreatedAt).First().Reason);
            toClose = candidates
                .Where(e => lastReason.TryGetValue(e.Id, out var reason) && IsLeaveMove(reason))
                .ToList();
        }

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

    /// <summary>The reasons the code writes when a leave moves an employee to OnLeave.</summary>
    internal static bool IsLeaveMove(string? reason) =>
        reason is not null
        && (reason.StartsWith("LeaveApproved:", StringComparison.Ordinal)
            || reason.StartsWith(ReasonPrefix, StringComparison.Ordinal));

    private const string ReasonPrefix = "LeaveScheduleReconciler:";

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
            Reason = $"{ReasonPrefix} {reason}"
        });
    }
}
