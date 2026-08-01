using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

public class ServiceTransactionStateHistory : AuditableEntity
{
    public Guid TransactionId { get; set; }
    public ServiceTransactionState FromState { get; set; }
    public ServiceTransactionState ToState { get; set; }
    public Guid? ChangedBy { get; set; }
    public string? Reason { get; set; }

    public ServiceTransaction Transaction { get; set; } = null!;
}
