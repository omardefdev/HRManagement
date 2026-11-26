using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

public sealed class PaymentOrderService(PayrollDbContext db)
{
    /// <summary>
    /// The orders of a batch that has none yet, by the issuer's rule (PLAN-58: one per line with a positive net, to the pinned account,
    /// <c>NO-ACCOUNT</c> held when the line pinned none); an idempotency key that exists already is skipped. Sends nothing.
    /// </summary>
    public async Task<IReadOnlyList<PaymentOrder>> EnsureOrdersForBatchAsync(
        Guid paymentBatchId,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.PaymentBatches
            .Include(b => b.PayrollRun)
            .ThenInclude(r => r!.SalaryLines)
            .FirstOrDefaultAsync(b => b.Id == paymentBatchId, cancellationToken)
            ?? throw new InvalidOperationException("دفعة الصرف غير موجودة");

        var existing = (await db.PaymentOrders.AsNoTracking().Where(o => o.PayrollRunId == batch.PayrollRunId)
            .Select(o => o.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var created = (await PaymentBatchIssuer.OrdersForAsync(db.EmployeeBankAccounts, batch, batch.PayrollRun.SalaryLines.ToList(), cancellationToken))
            .Where(o => !existing.Contains(o.IdempotencyKey))
            .ToList();
        db.PaymentOrders.AddRange(created);
        if (created.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return created;
    }

    public static string BuildIdempotencyKey(Guid payrollRunId, Guid salaryLineId) =>
        PaymentOrder.IdempotencyKeyFor(payrollRunId, salaryLineId);

    public async Task<bool> TryAdvanceStatusAsync(
        Guid orderId,
        PaymentOrderStatus target,
        string? externalReference = null,
        string? rejectionReason = null,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PaymentOrders.FindAsync([orderId], cancellationToken);
        if (order is null)
            return false;

        if (!PaymentOrderTransitions.CanTransition(order.Status, target))
            return false;

        order.Status = target;
        order.ExternalReference = externalReference ?? order.ExternalReference;
        order.RejectionReason = rejectionReason ?? order.RejectionReason;
        var now = DateTime.UtcNow;
        switch (target)
        {
            case PaymentOrderStatus.Submitted:
                order.SubmittedAt ??= now;
                break;
            case PaymentOrderStatus.Acknowledged:
                order.AcknowledgedAt ??= now;
                break;
            case PaymentOrderStatus.Settled:
            case PaymentOrderStatus.PartiallySettled:
                order.SettledAt ??= now;
                order.SettlementReference = externalReference ?? order.SettlementReference;
                break;
            case PaymentOrderStatus.Rejected:
                order.RejectedAt ??= now;
                break;
            case PaymentOrderStatus.Cancelled:
            case PaymentOrderStatus.Held:
            case PaymentOrderStatus.Draft:
                // PLAN-56/58: nothing was sent and nothing was paid; the order keeps no date of its own (its audit row has it).
                break;
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
