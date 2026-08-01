namespace HRManagement.MessageContracts.Events;

public interface IProbationEvaluationCompleted
{
    Guid EmployeeId { get; }
    Guid ContractId { get; }
    string Result { get; }
    Guid? EvaluatedBy { get; }
    DateTime EvaluatedAt { get; }
    string? Notes { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
