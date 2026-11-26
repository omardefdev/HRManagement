using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

/// <summary>
/// PLAN-58: the orders' side of a batch's transitions, on tracked rows; nothing is saved here (the handler saves the batch, its orders
/// and its bank file once, in one transaction).
/// </summary>
public sealed class PaymentOrderCoordinator(PayrollDbContext db) : IPaymentOrderCoordinator
{
    public async Task<IReadOnlyList<PaymentOrder>> EnsureAndSubmitOrdersAsync(
        PaymentBatch batch,
        DisbursementGateResult gate,
        PaymentSending sending,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(gate);

        var orders = await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id).ToListAsync(cancellationToken);
        if (orders.Count == 0 && batch.Source != PaymentBatchSource.PayrollRunPosted && sending == PaymentSending.Initial)
        {
            // A batch posted before PLAN-58 has no orders: they are made now by the issuer's own rule (pinned account, NO-ACCOUNT held).
            var lines = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == batch.PayrollRunId).ToListAsync(cancellationToken);
            var existingKeys = (await db.PaymentOrders.AsNoTracking().Where(o => o.PayrollRunId == batch.PayrollRunId)
                .Select(o => o.IdempotencyKey).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            foreach (var order in await PaymentBatchIssuer.OrdersForAsync(db.EmployeeBankAccounts, batch, lines, cancellationToken))
            {
                if (existingKeys.Contains(order.IdempotencyKey))
                    continue;
                db.PaymentOrders.Add(order);
                orders.Add(order);
            }
        }

        var now = DateTime.UtcNow;
        var sent = new List<PaymentOrder>();
        foreach (var order in orders.OrderBy(o => o.EmployeeId).ThenBy(o => o.Id))
        {
            var eligible = sending == PaymentSending.Initial
                ? order.Status is PaymentOrderStatus.Draft
                : order.Status is PaymentOrderStatus.Held;
            if (!eligible)
                continue;

            if (gate.ByEmployee.TryGetValue(order.EmployeeId, out var held))
            {
                Move(order, PaymentOrderStatus.Held);
                order.HoldReason = held.Reason;
                continue;
            }

            // An order that pinned no account stays held until a verified account is pinned on it (PLAN-58 §6 rule 3).
            if (string.IsNullOrWhiteSpace(order.BeneficiaryIban))
            {
                Move(order, PaymentOrderStatus.Held);
                order.HoldReason = PaymentOrderHoldReasons.NoAccount;
                continue;
            }

            Move(order, PaymentOrderStatus.Submitted);
            order.HoldReason = null;
            order.SubmittedAt ??= now;
            sent.Add(order);
        }

        return sent;
    }

    public async Task<IReadOnlyList<PaymentOrder>> AcknowledgeOrdersAsync(Guid paymentBatchId, string? channelReference, CancellationToken cancellationToken = default)
    {
        var orders = await db.PaymentOrders
            .Where(o => o.PaymentBatchId == paymentBatchId && o.Status == PaymentOrderStatus.Submitted)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var order in orders)
        {
            Move(order, PaymentOrderStatus.Acknowledged);
            order.AcknowledgedAt ??= now;
            order.ExternalReference = channelReference ?? order.ExternalReference;
        }

        return orders;
    }

    public async Task<IReadOnlyList<PaymentOrder>> SettleOrdersAsync(Guid paymentBatchId, string settlementReference, CancellationToken cancellationToken = default)
    {
        var orders = await db.PaymentOrders
            .Where(o => o.PaymentBatchId == paymentBatchId && o.Status == PaymentOrderStatus.Acknowledged)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var order in orders)
        {
            Move(order, PaymentOrderStatus.Settled);
            order.SettledAt ??= now;
            order.SettlementReference = settlementReference;
            order.ExternalReference = settlementReference;
        }

        return orders;
    }

    public async Task<IReadOnlyList<PaymentOrder>> RejectOrdersAsync(Guid paymentBatchId, string reason, CancellationToken cancellationToken = default)
    {
        var orders = await db.PaymentOrders
            .Where(o => o.PaymentBatchId == paymentBatchId
                        && (o.Status == PaymentOrderStatus.Submitted || o.Status == PaymentOrderStatus.Acknowledged))
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var order in orders)
        {
            Move(order, PaymentOrderStatus.Rejected);
            order.RejectedAt ??= now;
            order.RejectionReason = reason;
        }

        return orders;
    }

    private static void Move(PaymentOrder order, PaymentOrderStatus target)
    {
        if (order.Status == target)
            return;
        if (!PaymentOrderTransitions.CanTransition(order.Status, target))
            throw new InvalidOperationException($"لا يمكن نقل أمر الدفع من {order.Status} إلى {target}.");
        order.Status = target;
        order.UpdatedAt = DateTime.UtcNow;
    }
}
