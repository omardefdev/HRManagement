namespace HRManagement.MessageContracts.Events;

public interface IContractActivated
{
    Guid ContractId { get; }
    Guid EmployeeId { get; }
    string ContractType { get; }
    DateTime StartDate { get; }
    DateTime? EndDate { get; }
    decimal? BaseSalary { get; }
    string? PayGradeCode { get; }
    string? CorrelationId { get; }
    string? SourceSystem { get; }

    Guid? TenantId => null;


    int SchemaVersion => 2;
}
