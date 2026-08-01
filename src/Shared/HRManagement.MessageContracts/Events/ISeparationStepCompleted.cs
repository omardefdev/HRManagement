namespace HRManagement.MessageContracts.Events;

public interface ISeparationStepCompleted
{
    Guid SeparationRecordId { get; }
    Guid EmployeeId { get; }
    string StepName { get; }
    DateTime CompletedAt { get; }
    string? Notes { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
