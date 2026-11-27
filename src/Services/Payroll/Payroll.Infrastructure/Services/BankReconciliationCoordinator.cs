using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

public sealed class BankReconciliationCoordinator(
    PayrollDbContext db,
    BankReconciliationService reconciliation) : IBankReconciliationCoordinator
{
    /// <summary>
    /// PLAN-58 (§7): the manual confirmation records one statement line for the batch's settlement — the bank statement's reference and
    /// the settled amount — and the orders it settled carry the same reference. Importing the channel's statements and matching each
    /// order by its reference are PAY-I's (wave 2). The same reference is recorded once.
    /// </summary>
    public async Task<int> ReconcileBatchAsync(Guid paymentBatchId, CancellationToken cancellationToken = default)
    {
        var batch = await db.PaymentBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == paymentBatchId, cancellationToken);
        if (batch?.SettlementReference is not { Length: > 0 } reference)
            return 0;
        if (await db.BankStatementLines.AnyAsync(l => l.StatementReference == reference, cancellationToken))
            return 0;

        var settled = await db.PaymentOrders.AsNoTracking()
            .Where(o => o.PaymentBatchId == paymentBatchId && o.Status == PaymentOrderStatus.Settled && o.SettlementReference == reference)
            .SumAsync(o => o.Amount, cancellationToken);
        await reconciliation.ImportLineAsync(
            statementReference: reference,
            amount: settled,
            valueDate: batch.SettledAt ?? DateTime.UtcNow,
            bankReference: reference,
            cancellationToken: cancellationToken);
        return 1;
    }
}
