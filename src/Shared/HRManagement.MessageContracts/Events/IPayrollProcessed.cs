namespace HRManagement.MessageContracts.Events;

public interface IPayrollProcessed
{
    Guid PayrollRunId { get; }
    DateTime PeriodStart { get; }
    DateTime PeriodEnd { get; }
    decimal TotalNetPay { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
