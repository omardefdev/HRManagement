using Microsoft.EntityFrameworkCore;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
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
}
