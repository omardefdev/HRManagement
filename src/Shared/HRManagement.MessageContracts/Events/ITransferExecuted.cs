namespace HRManagement.MessageContracts.Events;

public interface ITransferExecuted
{
    Guid EmployeeId { get; }
    Guid FromOrgUnitId { get; }
    Guid ToOrgUnitId { get; }
    DateTime ExecutedAt { get; }

    Guid? TransferRequestId => null;
    decimal? SalaryChange => null;
    decimal? AllowanceChange => null;
    Guid? CorrelationId => null;
    string? SourceSystem => null;

    Guid? TenantId => null;

    /// <summary>SYR-008: HRCore service regime before transfer.</summary>
    string? FromLegalRegimeId => null;

    /// <summary>SYR-008: target service regime when cross-regime transfer.</summary>
    string? ToLegalRegimeId => null;

    /// <summary>SYR-008: mapped payroll regime code after transfer.</summary>
    string? PayrollRegimeCode => null;

    string? TransitionDecisionRef => null;

    string? ConditionCode => null;

    string? ConflictNotes => null;

    int SchemaVersion => 3;
}
