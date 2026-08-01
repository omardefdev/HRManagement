namespace HRManagement.MessageContracts.Events;

public interface ITransactionArchived
{
    Guid TransactionId { get; }
    Guid EmployeeId { get; }
    DateTime ArchivedAt { get; }
    string ArchiveRef { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
