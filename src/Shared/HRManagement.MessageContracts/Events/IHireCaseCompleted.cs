namespace HRManagement.MessageContracts.Events;

public interface IHireCaseCompleted
{
    Guid EmployeeId { get; }
    Guid ServiceTransactionId { get; }
    DateTime CompletedAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }
    Guid? ContractId { get; }
    Guid? RecruitmentApplicationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
