using System.ComponentModel.DataAnnotations;

namespace Payroll.Domain.Enums;

public enum PayrollRunJobStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "قيد التنفيذ")]
    Running = 1,
    [Display(Name = "متوقف مؤقتاً")]
    Paused = 2,
    [Display(Name = "مكتمل")]
    Completed = 3,
    [Display(Name = "فشل")]
    Failed = 4,
    [Display(Name = "ملغى")]
    Cancelled = 5
}

public enum PayrollRunJobPhase
{
    [Display(Name = "لقطة")]
    Snapshot = 0,
    [Display(Name = "حساب")]
    Calculate = 1,
    [Display(Name = "تحقق")]
    Validate = 2,
    [Display(Name = "ترحيل")]
    Post = 3,
    [Display(Name = "صرف")]
    Pay = 4
}

public enum PayrollRunPartitionStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,
    [Display(Name = "قيد التنفيذ")]
    Running = 1,
    [Display(Name = "مكتمل")]
    Completed = 2,
    [Display(Name = "فشل")]
    Failed = 3,
    [Display(Name = "ملغى")]
    Cancelled = 4
}

/// <summary>
/// Lifecycle of a disbursement batch. A batch only reaches a terminal paid state once the bank
/// or treasury has confirmed settlement; flipping straight from Pending to Processed reported
/// money as paid while no transfer had been attempted.
/// </summary>
public enum PaymentBatchStatus
{
    [Display(Name = "قيد الانتظار")]
    Pending = 0,

    /// <summary>Legacy terminal value. Retained so historical rows keep their meaning; new code
    /// must use <see cref="Settled"/>, which is only reachable through the banking cycle.</summary>
    [Display(Name = "مسدّد")]
    Processed = 1,

    [Display(Name = "فشل")]
    Failed = 2,
    [Display(Name = "معكوس")]
    Reversed = 3,

    /// <summary>Payment order handed to the channel; awaiting acknowledgement.</summary>
    [Display(Name = "مُرسَل")]
    Submitted = 4,

    /// <summary>Channel acknowledged receipt. Money has not moved yet.</summary>
    [Display(Name = "مُستَلَم")]
    Acked = 5,

    /// <summary>Channel confirmed settlement. This is the only state that means "paid".</summary>
    [Display(Name = "مسدّد")]
    Settled = 6,

    /// <summary>Channel rejected the order; nothing was paid.</summary>
    [Display(Name = "مرفوض")]
    Rejected = 7,

    /// <summary>Funds were returned after settlement (bad account, recall).</summary>
    [Display(Name = "مُرتَجَع")]
    Returned = 8
}

/// <summary>Legal transitions for <see cref="PaymentBatchStatus"/>.</summary>
public static class PaymentBatchTransitions
{
    private static readonly Dictionary<PaymentBatchStatus, PaymentBatchStatus[]> Allowed = new()
    {
        [PaymentBatchStatus.Pending] = [PaymentBatchStatus.Submitted, PaymentBatchStatus.Failed],
        [PaymentBatchStatus.Submitted] = [PaymentBatchStatus.Acked, PaymentBatchStatus.Rejected, PaymentBatchStatus.Failed],
        [PaymentBatchStatus.Acked] = [PaymentBatchStatus.Settled, PaymentBatchStatus.Rejected, PaymentBatchStatus.Failed],
        [PaymentBatchStatus.Settled] = [PaymentBatchStatus.Returned, PaymentBatchStatus.Reversed],
        [PaymentBatchStatus.Processed] = [PaymentBatchStatus.Reversed],
        [PaymentBatchStatus.Rejected] = [PaymentBatchStatus.Submitted],
        [PaymentBatchStatus.Returned] = [PaymentBatchStatus.Submitted],
        [PaymentBatchStatus.Failed] = [PaymentBatchStatus.Submitted],
        [PaymentBatchStatus.Reversed] = []
    };

    public static bool CanTransition(PaymentBatchStatus from, PaymentBatchStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>States in which money is considered to have left the treasury.</summary>
    public static bool IsPaid(PaymentBatchStatus status) =>
        status is PaymentBatchStatus.Settled or PaymentBatchStatus.Processed;
}

public enum PaymentBatchKind
{
    [Display(Name = "صرف")]
    Disbursement = 0,
    [Display(Name = "عكس")]
    Reversal = 1
}
