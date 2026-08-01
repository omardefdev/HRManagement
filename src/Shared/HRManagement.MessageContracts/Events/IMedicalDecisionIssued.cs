namespace HRManagement.MessageContracts.Events;

public interface IMedicalDecisionIssued
{
    Guid EmployeeId { get; }
    DateTime EffectiveDate { get; }
    string MedicalRef { get; }
    string? Notes { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
