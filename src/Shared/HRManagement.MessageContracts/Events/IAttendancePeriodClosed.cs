namespace HRManagement.MessageContracts.Events;

/// <summary>
/// The attendance close's event until PLAN-57. <b>Nothing publishes it any more and nothing consumes it</b>: it carried a random
/// transaction id and no version, so payroll took the first close only. The contract stays one release for a message still in a
/// queue of an older deployment; <see cref="IAttendanceSummaryPublished"/> replaces it.
/// </summary>
[Obsolete("PLAN-57: replaced by IAttendanceSummaryPublished (a versioned summary per employee and period). Nothing publishes or consumes it.")]
public interface IAttendancePeriodClosed
{
    Guid TransactionId { get; }
    Guid EmployeeId { get; }
    DateOnly PeriodStart { get; }
    DateOnly PeriodEnd { get; }
    int PresentDays { get; }
    int AbsentDays { get; }
    int OnLeaveDays { get; }
    int RemoteDays { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
