namespace HRManagement.MessageContracts.Events;

public interface ITransactionRegisteredInDiwan
{
    Guid TransactionId { get; }
    string DiwanNumber { get; }
    DateTime RegisteredAt { get; }
    string SourceSystem { get; }
    string? CorrelationId { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
