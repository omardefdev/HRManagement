using Microsoft.EntityFrameworkCore;
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

        var profiles = await db.EmployeePayProfiles
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.EmployeeId, p.BankAccountIban, p.BankAccountVerifiedAt })
            .ToListAsync(cancellationToken);
        var ibans = profiles
            .Where(p => p.BankAccountVerifiedAt is not null && !string.IsNullOrWhiteSpace(p.BankAccountIban))
            .ToDictionary(p => p.EmployeeId, p => p.BankAccountIban!);

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
                BeneficiaryIban = ibans.GetValueOrDefault(line.EmployeeId),
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
