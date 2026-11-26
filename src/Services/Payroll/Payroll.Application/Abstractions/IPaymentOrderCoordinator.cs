using Payroll.Domain.Entities;

namespace Payroll.Application.Abstractions;

/// <summary>PLAN-58 (§1, §2): which sending of a batch: its first (<c>process</c>), or the orders released after it (<c>submit-held</c>).</summary>
public enum PaymentSending
{
    Initial = 0,
    HeldRelease = 1,
}

/// <summary>
/// Keeps <see cref="PaymentOrder"/> lifecycle aligned with <see cref="PaymentBatch"/> disbursement transitions. PLAN-58: every method
/// changes tracked orders and saves nothing; the caller saves once, with the batch and its file, in one transaction.
/// </summary>
public interface IPaymentOrderCoordinator
{
    /// <summary>
    /// The one sending of orders (<c>Pay058SinglePaymentPathArchitectureTests</c>: called by <c>ProcessPaymentBatchHandler</c> and
    /// <c>SubmitHeldPaymentOrdersHandler</c> only). An order whose employee the gate holds becomes (or stays) <c>Held</c> with its reason;
    /// the others become <c>Submitted</c>. Returns the orders submitted in this sending (what its bank file carries). A batch older than
    /// PLAN-58 that has no orders gets them first, by the issuer's own rule.
    /// </summary>
    Task<IReadOnlyList<PaymentOrder>> EnsureAndSubmitOrdersAsync(
        PaymentBatch batch,
        DisbursementGateResult gate,
        PaymentSending sending,
        CancellationToken cancellationToken = default);

    /// <summary>The channel acknowledged the sent orders (<c>Submitted</c> → <c>Acknowledged</c>). Returns them.</summary>
    Task<IReadOnlyList<PaymentOrder>> AcknowledgeOrdersAsync(Guid paymentBatchId, string? channelReference, CancellationToken cancellationToken = default);

    /// <summary>The bank statement confirmed the acknowledged orders (<c>Acknowledged</c> → <c>Settled</c>). Returns them.</summary>
    Task<IReadOnlyList<PaymentOrder>> SettleOrdersAsync(Guid paymentBatchId, string settlementReference, CancellationToken cancellationToken = default);

    /// <summary>The channel rejected the sent orders (<c>Submitted</c>/<c>Acknowledged</c> → <c>Rejected</c>). Returns them.</summary>
    Task<IReadOnlyList<PaymentOrder>> RejectOrdersAsync(Guid paymentBatchId, string reason, CancellationToken cancellationToken = default);
}
