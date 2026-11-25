namespace HRManagement.MessageContracts.Events;

/// <summary>
/// PLAN-56 (G-PAY-06): a posted payroll run was reversed by an approved request. The run's <see cref="IPayrollProcessed"/> said what
/// was posted; this says that it was taken back, and how much. Together the two give the run's net effect: nothing.
/// It is published only for a run whose payment batch had not left for the bank (D-PAY-16): no money is recovered by it.
/// </summary>
public interface IPayrollReversed
{
    Guid PayrollRunId { get; }
    DateTime PeriodStart { get; }
    DateTime PeriodEnd { get; }

    /// <summary>The total the posting published as <see cref="IPayrollProcessed.TotalNetPay"/>: the amount this reversal takes back.</summary>
    decimal ReversedNetPay { get; }

    /// <summary>The request whose approval reversed the run.</summary>
    Guid ReversalRequestId { get; }

    DateTime ReversedAt { get; }

    Guid? TenantId => null;

    int SchemaVersion => 1;
}
