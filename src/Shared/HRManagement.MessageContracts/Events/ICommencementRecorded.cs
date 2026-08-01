namespace HRManagement.MessageContracts.Events;

public interface ICommencementRecorded
{
    Guid TransactionId { get; }
    Guid EmployeeId { get; }
    DateTime StartDate { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
