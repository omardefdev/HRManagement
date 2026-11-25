using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.BankAccounts;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

public sealed class PaymentOrderService(PayrollDbContext db)
{
    /// <summary>Creates one order per salary line; skips when idempotency key already exists (48h dedup window).</summary>
    public async Task<IReadOnlyList<PaymentOrder>> EnsureOrdersForBatchAsync(
        Guid paymentBatchId,
        IReadOnlyDictionary<Guid, decimal>? amountOverrides = null,
        CancellationToken cancellationToken = default)
    {
        var batch = await db.PaymentBatches
            .Include(b => b.PayrollRun)
            .ThenInclude(r => r!.SalaryLines)
            .FirstOrDefaultAsync(b => b.Id == paymentBatchId, cancellationToken)
            ?? throw new InvalidOperationException("دفعة الصرف غير موجودة");

        // PLAN-55 (D-PAY-15): the destination is the account each line pinned at its run's snapshot, never the pay profile and never
        // the employee's current account. A line without a pinned account gets an order without a destination (PLAN-58 decides what
        // the disbursement path does with it). A local account number goes into the same field as an IBAN.
        var pinned = await BankAccountPinning.PinnedAccountsAsync(db.EmployeeBankAccounts, batch.PayrollRun.SalaryLines, cancellationToken);

        var created = new List<PaymentOrder>();
        foreach (var line in batch.PayrollRun.SalaryLines)
        {
            var amount = amountOverrides?.TryGetValue(line.EmployeeId, out var overrideAmount) == true
                ? overrideAmount
                : line.NetPay;
            if (amount <= 0)
                continue;

            var key = BuildIdempotencyKey(batch.PayrollRunId, line.Id);
            if (await db.PaymentOrders.AnyAsync(o => o.IdempotencyKey == key, cancellationToken))
                continue;

            var order = new PaymentOrder
            {
                IdempotencyKey = key,
                PayrollRunId = batch.PayrollRunId,
                PaymentBatchId = batch.Id,
                SalaryLineId = line.Id,
                EmployeeId = line.EmployeeId,
                Amount = amount,
                BeneficiaryIban = line.BankAccountId is Guid accountId && pinned.TryGetValue(accountId, out var account)
                    ? account.AccountNumber
                    : null,
                Status = PaymentOrderStatus.Draft
            };
            db.PaymentOrders.Add(order);
            created.Add(order);
        }

        if (created.Count > 0)
            await db.SaveChangesAsync(cancellationToken);

        return created;
    }

    public static string BuildIdempotencyKey(Guid payrollRunId, Guid salaryLineId) =>
        $"payroll:{payrollRunId:N}:{salaryLineId:N}";

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

        if (!CanTransition(order.Status, target))
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
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool CanTransition(PaymentOrderStatus current, PaymentOrderStatus target) =>
        (current, target) switch
        {
            (PaymentOrderStatus.Draft, PaymentOrderStatus.Submitted) => true,
            (PaymentOrderStatus.Submitted, PaymentOrderStatus.Acknowledged) => true,
            (PaymentOrderStatus.Submitted, PaymentOrderStatus.Rejected) => true,
            (PaymentOrderStatus.Acknowledged, PaymentOrderStatus.Settled) => true,
            (PaymentOrderStatus.Acknowledged, PaymentOrderStatus.PartiallySettled) => true,
            (PaymentOrderStatus.Acknowledged, PaymentOrderStatus.Rejected) => true,
            _ => false
        };
}
