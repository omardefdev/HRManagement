using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Web.Controllers;

/// <summary>PLAN-58 (§6): one held employee of a batch, as the held report shows him.</summary>
public sealed record PaymentBatchHeldRow(
    Guid EmployeeId, string? EmployeeName, string? Reason, decimal Amount, Guid? HoldId, Guid? PendingRequestId);

/// <summary>
/// PLAN-58 (§6, P-10): the held report of a batch: its <c>Held</c> orders, the active hold of each employee (of the batch's run or with no
/// run) and the four-eyes release request that waits on it. Shared by the API (<c>GET …/held</c>) and the batch's page.
/// </summary>
public static class PaymentBatchHeldReport
{
    public static async Task<IReadOnlyList<PaymentBatchHeldRow>> BuildAsync(
        PayrollDbContext db, PaymentBatch batch, IEmployeeLookup employeeLookup, CancellationToken ct)
    {
        var orders = await db.PaymentOrders.AsNoTracking()
            .Where(o => o.PaymentBatchId == batch.Id && o.Status == PaymentOrderStatus.Held)
            .Select(o => new { o.EmployeeId, o.HoldReason, o.Amount })
            .ToListAsync(ct);
        if (orders.Count == 0)
            return [];

        var employees = orders.Select(o => o.EmployeeId).Distinct().ToList();
        var holds = await db.PaymentHolds.AsNoTracking()
            .Where(h => employees.Contains(h.EmployeeId) && h.Status == PaymentHoldStatus.Active
                        && (h.PayrollRunId == null || h.PayrollRunId == batch.PayrollRunId))
            .Select(h => new { h.Id, h.EmployeeId })
            .ToListAsync(ct);
        var holdIds = holds.Select(h => h.Id).ToList();
        var pending = await db.PayrollDualControlRequests.AsNoTracking()
            .Where(r => r.Kind == PayrollDualControlKind.HoldRelease && r.Status == PayrollDualControlStatus.Pending && holdIds.Contains(r.TargetId))
            .Select(r => new { r.Id, r.TargetId })
            .ToListAsync(ct);
        var names = await employeeLookup.GetEmployeeDisplayNamesAsync(employees, ct);

        return orders
            .Select(o =>
            {
                var hold = holds.FirstOrDefault(h => h.EmployeeId == o.EmployeeId);
                var request = hold is null ? null : pending.FirstOrDefault(r => r.TargetId == hold.Id);
                return new PaymentBatchHeldRow(o.EmployeeId, names.GetValueOrDefault(o.EmployeeId), o.HoldReason, o.Amount, hold?.Id, request?.Id);
            })
            .OrderBy(r => r.EmployeeName ?? "￿", StringComparer.Ordinal)
            .ToList();
    }
}
