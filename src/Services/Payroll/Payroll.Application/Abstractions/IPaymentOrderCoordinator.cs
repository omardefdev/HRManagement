namespace Payroll.Application.Abstractions;

/// <summary>
/// Keeps <see cref="Payroll.Domain.Entities.PaymentOrder"/> lifecycle aligned with
/// <see cref="Payroll.Domain.Entities.PaymentBatch"/> disbursement transitions.
/// </summary>
public interface IPaymentOrderCoordinator
{
    Task EnsureAndSubmitOrdersAsync(Guid paymentBatchId, CancellationToken cancellationToken = default);

    Task AcknowledgeOrdersAsync(Guid paymentBatchId, string? channelReference, CancellationToken cancellationToken = default);

    Task SettleOrdersAsync(Guid paymentBatchId, string settlementReference, CancellationToken cancellationToken = default);
}
