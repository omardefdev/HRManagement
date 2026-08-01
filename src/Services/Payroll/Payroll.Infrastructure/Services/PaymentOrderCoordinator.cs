using Microsoft.EntityFrameworkCore;
using Payroll.Application.Abstractions;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Services;

public sealed class PaymentOrderCoordinator(
    PayrollDbContext db,
    PaymentOrderService paymentOrders) : IPaymentOrderCoordinator
{
    public async Task EnsureAndSubmitOrdersAsync(Guid paymentBatchId, CancellationToken cancellationToken = default)
    {
        var orders = await paymentOrders.EnsureOrdersForBatchAsync(paymentBatchId, cancellationToken);
        foreach (var order in orders)
            await paymentOrders.TryAdvanceStatusAsync(order.Id, PaymentOrderStatus.Submitted, cancellationToken: cancellationToken);

        var pending = await db.PaymentOrders
            .Where(o => o.PaymentBatchId == paymentBatchId && o.Status == PaymentOrderStatus.Draft)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);
        foreach (var orderId in pending)
            await paymentOrders.TryAdvanceStatusAsync(orderId, PaymentOrderStatus.Submitted, cancellationToken: cancellationToken);
    }

    public async Task AcknowledgeOrdersAsync(Guid paymentBatchId, string? channelReference, CancellationToken cancellationToken = default)
    {
        var orderIds = await db.PaymentOrders
            .Where(o => o.PaymentBatchId == paymentBatchId && o.Status == PaymentOrderStatus.Submitted)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
            await paymentOrders.TryAdvanceStatusAsync(
                orderId,
                PaymentOrderStatus.Acknowledged,
                externalReference: channelReference,
                cancellationToken: cancellationToken);
    }

    public async Task SettleOrdersAsync(Guid paymentBatchId, string settlementReference, CancellationToken cancellationToken = default)
    {
        var orderIds = await db.PaymentOrders
            .Where(o => o.PaymentBatchId == paymentBatchId
                        && (o.Status == PaymentOrderStatus.Acknowledged || o.Status == PaymentOrderStatus.Submitted))
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        foreach (var orderId in orderIds)
            await paymentOrders.TryAdvanceStatusAsync(
                orderId,
                PaymentOrderStatus.Settled,
                externalReference: settlementReference,
                cancellationToken: cancellationToken);
    }
}
