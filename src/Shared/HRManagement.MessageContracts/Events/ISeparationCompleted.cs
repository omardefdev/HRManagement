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

    /// <summary>SYR-009: HRCore service regime at separation.</summary>
    string? SourceLegalRegimeId => null;

    /// <summary>SYR-009: mapped payroll regime for EOS config selection.</summary>
    string? PayrollRegimeCode => null;

    int SchemaVersion => 3;
}
