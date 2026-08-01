namespace HRManagement.MessageContracts.Events;

public interface IJobChangeRequested
{
    Guid RequestId { get; }
    Guid EmployeeId { get; }
    string CurrentTitle { get; }
    string NewTitle { get; }
    DateTime EffectiveDate { get; }
    string? Reason { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
