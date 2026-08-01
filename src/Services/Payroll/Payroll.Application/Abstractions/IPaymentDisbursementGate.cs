using Payroll.Domain.Entities;

namespace Payroll.Application.Abstractions;

/// <summary>Outcome of the pre-disbursement control evaluation.</summary>
public readonly record struct DisbursementGateResult(bool Allowed, string? Reason)
{
    public static DisbursementGateResult Allow() => new(true, null);
    public static DisbursementGateResult Block(string reason) => new(false, reason);
}

/// <summary>
/// Controls that must pass before a payment batch may be handed to a disbursement channel.
/// <para>
/// Payment holds and fraud alerts were implemented but never consulted by the paying code, so a
/// held or flagged employee was paid anyway. Submission now runs through this gate.
/// </para>
/// </summary>
public interface IPaymentDisbursementGate
{
    Task<DisbursementGateResult> EvaluateAsync(PaymentBatch batch, CancellationToken cancellationToken);
}
