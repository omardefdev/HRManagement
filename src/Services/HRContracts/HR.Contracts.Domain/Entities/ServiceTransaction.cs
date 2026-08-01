using HR.Contracts.Domain.Enums;
using HRManagement.SharedKernel.Entities;

namespace HR.Contracts.Domain.Entities;

public class ServiceTransaction : AuditableEntity
{
    private static readonly IReadOnlyDictionary<ServiceTransactionState, ServiceTransactionState[]> AllowedTransitions =
        new Dictionary<ServiceTransactionState, ServiceTransactionState[]>
        {
            [ServiceTransactionState.Draft] = [ServiceTransactionState.Submitted, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.Submitted] = [ServiceTransactionState.UnderReview, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.UnderReview] = [ServiceTransactionState.NeedsInfo, ServiceTransactionState.InProcess, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.NeedsInfo] = [ServiceTransactionState.UnderReview, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.InProcess] = [ServiceTransactionState.PendingApproval, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.PendingApproval] = [ServiceTransactionState.Approved, ServiceTransactionState.Rejected, ServiceTransactionState.InProcess],
            [ServiceTransactionState.Rejected] = [ServiceTransactionState.InProcess, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.Approved] = [ServiceTransactionState.Executing, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.Executing] = [ServiceTransactionState.PostProcessing, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.PostProcessing] = [ServiceTransactionState.Completed, ServiceTransactionState.Cancelled],
            [ServiceTransactionState.Completed] = [ServiceTransactionState.Archived],
            [ServiceTransactionState.Archived] = [],
            [ServiceTransactionState.Cancelled] = []
        };

    public Guid EmployeeId { get; set; }
    public ServiceTransactionType TransactionType { get; set; }
    public ServiceTransactionState State { get; private set; } = ServiceTransactionState.Draft;
    public string? ReferenceNumber { get; set; }
    public string? Notes { get; set; }
    public Guid? CurrentApproverId { get; set; }

    public ICollection<ServiceTransactionStateHistory> StateHistory { get; set; } = new List<ServiceTransactionStateHistory>();

    public void MoveTo(ServiceTransactionState nextState, Guid? byUserId, string? reason = null)
    {
        if (State == nextState)
        {
            return;
        }

        if (!AllowedTransitions.TryGetValue(State, out var allowed) || !allowed.Contains(nextState))
        {
            throw new InvalidOperationException($"Invalid state transition from {State} to {nextState}.");
        }

        StateHistory.Add(new ServiceTransactionStateHistory
        {
            TransactionId = Id,
            FromState = State,
            ToState = nextState,
            ChangedBy = byUserId,
            Reason = reason
        });

        State = nextState;
    }
}
