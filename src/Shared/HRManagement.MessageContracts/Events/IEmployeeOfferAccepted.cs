namespace HRManagement.MessageContracts.Events;

public interface IEmployeeOfferAccepted
{
    Guid EmployeeId { get; }
    string EmploymentCategory { get; }
    DateTime ExpectedStartDate { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
