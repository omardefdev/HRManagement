namespace HRManagement.SharedKernel.Interfaces;

public interface IDomainEvent
{
    DateTime OccurredOn { get; }
    Guid EventId { get; }
}
