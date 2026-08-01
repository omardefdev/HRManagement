namespace HRManagement.MessageContracts.Events;

public interface IConfirmationApproved
{
    Guid TransactionId { get; }
    Guid EmployeeId { get; }
    Guid ContractId { get; }
    string Result { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
