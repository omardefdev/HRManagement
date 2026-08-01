namespace HRManagement.MessageContracts.Events;

public interface IResignationSubmitted
{
    Guid EmployeeId { get; }
    DateTime EffectiveDate { get; }
    string? Reason { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
