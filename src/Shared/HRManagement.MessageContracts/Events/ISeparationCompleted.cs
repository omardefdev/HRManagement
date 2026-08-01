namespace HRManagement.MessageContracts.Events;

public interface ISeparationCompleted
{
    Guid EmployeeId { get; }
    string SeparationType { get; }
    DateTime EffectiveDate { get; }
    DateTime OccurredAt { get; }
    string? CorrelationId { get; }
    string SourceSystem { get; }
    Guid? ContractId { get; }
    Guid? SeparationRecordId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
