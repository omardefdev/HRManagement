namespace HRManagement.MessageContracts.Events;

public interface IAdvanceApproved
{
    Guid TransactionId { get; }
    Guid AdvanceRequestId { get; }
    Guid EmployeeId { get; }
    decimal Amount { get; }
    Guid ApprovedBy { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }
    decimal MonthlyInstallment { get; }
    int NumberOfInstallments { get; }

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
