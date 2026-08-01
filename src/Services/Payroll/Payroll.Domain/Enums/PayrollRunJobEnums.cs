namespace Payroll.Domain.Enums;

public enum PayrollRunJobStatus
{
    Pending = 0,
    Running = 1,
    Paused = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

public enum PayrollRunJobPhase
{
    Snapshot = 0,
    Calculate = 1,
    Validate = 2,
    Post = 3,
    Pay = 4
}

public enum PayrollRunPartitionStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}

/// <summary>
/// Lifecycle of a disbursement batch. A batch only reaches a terminal paid state once the bank
/// or treasury has confirmed settlement; flipping straight from Pending to Processed reported
/// money as paid while no transfer had been attempted.
/// </summary>
public enum PaymentBatchStatus
{
    Pending = 0,

    /// <summary>Legacy terminal value. Retained so historical rows keep their meaning; new code
    /// must use <see cref="Settled"/>, which is only reachable through the banking cycle.</summary>
    Processed = 1,

    Failed = 2,
    Reversed = 3,

    /// <summary>Payment order handed to the channel; awaiting acknowledgement.</summary>
    Submitted = 4,

    /// <summary>Channel acknowledged receipt. Money has not moved yet.</summary>
    Acked = 5,

    /// <summary>Channel confirmed settlement. This is the only state that means "paid".</summary>
    Settled = 6,

    /// <summary>Channel rejected the order; nothing was paid.</summary>
    Rejected = 7,

    /// <summary>Funds were returned after settlement (bad account, recall).</summary>
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
    Disbursement = 0,
    Reversal = 1
}
