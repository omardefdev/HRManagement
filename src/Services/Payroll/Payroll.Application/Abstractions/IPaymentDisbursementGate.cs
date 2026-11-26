using Payroll.Domain.Entities;

namespace Payroll.Application.Abstractions;

/// <summary>PLAN-58 (G-PAY-15): one employee the gate holds out of a sending: who, why (a <see cref="PaymentOrderHoldReasons"/> code), and
/// the record that holds him (the payment hold or the alert), when there is one.</summary>
public sealed record HeldEmployee(Guid EmployeeId, string Reason, Guid? Reference);

/// <summary>
/// The pre-disbursement controls' answer for one batch. PLAN-58 (D-PAY-4): a hold, an open critical alert, a retired pinned account or a
/// service-regime blocker holds that employee's order only (<see cref="Held"/>); the others are sent. Only a control that names no
/// employee (a critical alert with none) stops the whole batch (<see cref="StopReason"/>).
/// </summary>
public sealed record DisbursementGateResult(string? StopReason, IReadOnlyList<HeldEmployee> Held)
{
    public bool Stopped => StopReason is not null;

    /// <summary>The first reason found for each held employee.</summary>
    public IReadOnlyDictionary<Guid, HeldEmployee> ByEmployee { get; } =
        Held.GroupBy(h => h.EmployeeId).ToDictionary(g => g.Key, g => g.First());

    public static DisbursementGateResult Stop(string reason) => new(reason, []);

    public static DisbursementGateResult HoldOnly(IReadOnlyList<HeldEmployee> held) => new(null, held);
}

/// <summary>
/// Controls that must pass before a payment batch's orders may be handed to a disbursement channel. PLAN-58: they are read in the same
/// request that sends (<c>process</c>, <c>submit-held</c>), after the fraud scan, and answer per employee.
/// </summary>
public interface IPaymentDisbursementGate
{
    Task<DisbursementGateResult> EvaluateAsync(PaymentBatch batch, CancellationToken cancellationToken);
}
