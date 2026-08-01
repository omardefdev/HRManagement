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


    int SchemaVersion => 2;
}
