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
    Cancelled = 6
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
    /// </summary>
    public static bool HasLeft(PaymentOrderStatus status) =>
        status is PaymentOrderStatus.Submitted or PaymentOrderStatus.Acknowledged
            or PaymentOrderStatus.Settled or PaymentOrderStatus.PartiallySettled;
}
