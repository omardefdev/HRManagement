namespace HRManagement.MessageContracts.Events;

public interface ILegalJudgmentIssued
{
    Guid EmployeeId { get; }
    DateTime EffectiveDate { get; }
    string JudgmentRef { get; }
    string? Notes { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
