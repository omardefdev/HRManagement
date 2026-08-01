using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

public sealed class BankReconciliationCoordinator(
    PayrollDbContext db,
    BankReconciliationService reconciliation) : IBankReconciliationCoordinator
{
    public async Task<int> ReconcileBatchAsync(Guid paymentBatchId, CancellationToken cancellationToken = default)
    {
        var orders = await db.PaymentOrders.AsNoTracking()
            .Where(o => o.PaymentBatchId == paymentBatchId)
            .Where(o => o.Status == PaymentOrderStatus.Settled || o.Status == PaymentOrderStatus.Acknowledged)
            .Select(o => new { o.Amount, o.SettlementReference, o.ExternalReference })
            .ToListAsync(cancellationToken);

        var matched = 0;
        foreach (var order in orders)
        {
            var reference = order.SettlementReference ?? order.ExternalReference ?? $"batch:{paymentBatchId:N}";
            await reconciliation.ImportLineAsync(
                statementReference: reference,
                amount: order.Amount,
                valueDate: DateTime.UtcNow,
                bankReference: order.ExternalReference,
                cancellationToken: cancellationToken);
            matched++;
        }

        return matched + await reconciliation.MatchUnmatchedAsync(cancellationToken);
    }
}
