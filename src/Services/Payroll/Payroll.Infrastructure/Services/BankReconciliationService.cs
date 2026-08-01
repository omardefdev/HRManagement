using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

/// <summary>Matches imported bank lines to payment orders by reference and amount.</summary>
public sealed class BankReconciliationService(PayrollDbContext db)
{
    public async Task<BankStatementLine> ImportLineAsync(
        string statementReference,
        decimal amount,
        DateTime valueDate,
        string? bankReference = null,
        string? beneficiaryIban = null,
        CancellationToken cancellationToken = default)
    {
        var line = new BankStatementLine
        {
            StatementReference = statementReference,
            Amount = amount,
            ValueDate = valueDate,
            BankReference = bankReference,
            BeneficiaryIban = beneficiaryIban
        };
        db.BankStatementLines.Add(line);
        await db.SaveChangesAsync(cancellationToken);
        return line;
    }

    public async Task<int> MatchUnmatchedAsync(CancellationToken cancellationToken = default)
    {
        var unmatched = await db.BankStatementLines
            .Where(l => l.MatchedPaymentOrderId == null)
            .ToListAsync(cancellationToken);

        var matched = 0;
        foreach (var line in unmatched)
        {
            var order = await db.PaymentOrders
                .Where(o => o.Status == PaymentOrderStatus.Acknowledged || o.Status == PaymentOrderStatus.Submitted)
                .Where(o => o.Amount == line.Amount)
                .Where(o => line.BankReference == null
                            || o.ExternalReference == line.BankReference
                            || o.SettlementReference == line.BankReference)
                .OrderByDescending(o => o.SubmittedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (order is null)
                continue;

            line.MatchedPaymentOrderId = order.Id;
            line.MatchedAt = DateTime.UtcNow;
            order.Status = PaymentOrderStatus.Settled;
            order.SettledAt ??= line.ValueDate.ToUniversalTime();
            order.SettlementReference = line.BankReference ?? line.StatementReference;
            matched++;
        }

        if (matched > 0)
            await db.SaveChangesAsync(cancellationToken);

        return matched;
    }
}
