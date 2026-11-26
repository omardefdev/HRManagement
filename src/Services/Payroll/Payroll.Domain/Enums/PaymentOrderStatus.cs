using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

/// <summary>SYR-011 per-beneficiary payment order lifecycle.</summary>
public enum PaymentOrderStatus
{
    [Display(Name = "مسودة")]
    Draft = 0,
    [Display(Name = "مُرسَل")]
    Submitted = 1,
    [Display(Name = "مُقرّ")]
    Acknowledged = 2,
    [Display(Name = "مرفوض")]
    Rejected = 3,
    [Display(Name = "مسدّد")]
    Settled = 4,
    [Display(Name = "مسدّد جزئياً")]
    PartiallySettled = 5,

    /// <summary>
    /// PLAN-56 (D-PAY-16): an order that never left, cancelled with its batch when its run's reversal was approved. Final: nothing
    /// was paid by it, and it is not «disbursed».
    /// </summary>
    [Display(Name = "ملغى")]
    Cancelled = 6,

    /// <summary>
    /// PLAN-58 (D-PAY-4, G-PAY-15): an order kept out of the sending and out of the bank file because its employee is held (a payment
    /// hold, an open critical alert, a retired pinned account, a service-regime blocker) or has no pinned account
    /// (<see cref="Entities.PaymentOrder.HoldReason"/> says which). The other orders of the batch are sent. Nothing of it has left: it
    /// is sent later, alone, by a supplementary file (<c>submit-held</c>), or cancelled with its batch.
    /// </summary>
    [Display(Name = "محجوز")]
    Held = 7
}

/// <summary>
/// Legal transitions of a payment order. PLAN-56 (D-PAY-16): one table for the channel's cycle and for the cancellation of an order
/// that never left (its batch was voided by an approved reversal). Nothing leads out of <see cref="PaymentOrderStatus.Cancelled"/>.
/// </summary>
public static class PaymentOrderTransitions
{
    public static bool CanTransition(PaymentOrderStatus current, PaymentOrderStatus target) =>
        (current, target) switch
        {
            (PaymentOrderStatus.Draft, PaymentOrderStatus.Submitted) => true,
            (PaymentOrderStatus.Draft, PaymentOrderStatus.Cancelled) => true,
            // PLAN-58: a held order is outside the sending until its hold is lifted; it never left, so it can be cancelled with its batch.
            (PaymentOrderStatus.Draft, PaymentOrderStatus.Held) => true,
            (PaymentOrderStatus.Held, PaymentOrderStatus.Draft) => true,
            (PaymentOrderStatus.Held, PaymentOrderStatus.Submitted) => true,
            (PaymentOrderStatus.Held, PaymentOrderStatus.Cancelled) => true,
            (PaymentOrderStatus.Submitted, PaymentOrderStatus.Acknowledged) => true,
            (PaymentOrderStatus.Submitted, PaymentOrderStatus.Rejected) => true,
            (PaymentOrderStatus.Acknowledged, PaymentOrderStatus.Settled) => true,
            (PaymentOrderStatus.Acknowledged, PaymentOrderStatus.PartiallySettled) => true,
            (PaymentOrderStatus.Acknowledged, PaymentOrderStatus.Rejected) => true,
            _ => false
        };

    /// <summary>
    /// An order the channel holds or has paid: handed over, acknowledged, settled in full or in part. Its money has left or is on its
    /// way; it is never cancelled. A draft never left, and an order the channel rejected is the channel's record of nothing paid.
    /// PLAN-58: a <see cref="PaymentOrderStatus.Held"/> order never left either.
    /// </summary>
    public static bool HasLeft(PaymentOrderStatus status) =>
        status is PaymentOrderStatus.Submitted or PaymentOrderStatus.Acknowledged
            or PaymentOrderStatus.Settled or PaymentOrderStatus.PartiallySettled;
}
