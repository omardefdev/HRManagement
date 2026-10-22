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

    // ── FUN-029: explicit recruitment lineage (was parsed from Notes) and hire tracking.

    public Guid? RecruitmentApplicationId { get; set; }

    public Guid? RequisitionId { get; set; }

    public Guid? OfferLetterId { get; set; }

    /// <summary>FUN-029 (J): expected start date from the hire event; drives the commencement watch.</summary>
    public DateOnly? ExpectedStartDate { get; set; }

    /// <summary>FUN-029 (I3): the appointment decision issued for this hire.</summary>
    public Guid? AppointmentDecisionId { get; set; }

    /// <summary>FUN-029 (J): set once when commencement is overdue (intervention published).</summary>
    public DateTime? CommencementOverdueAt { get; set; }

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
