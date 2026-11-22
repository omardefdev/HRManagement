namespace HRManagement.MessageContracts.Events;

/// <summary>PLAN-53: an approver, who is not the requester, approved or rejected a salary change request.</summary>
public interface IPayProfileChangeDecided
{
    Guid RequestId { get; }
    Guid EmployeeId { get; }

    /// <summary><c>Approved</c> or <c>Rejected</c>.</summary>
    string Decision { get; }

    /// <summary>The request's state after the decision: <c>Applied</c>, <c>Scheduled</c> or <c>Rejected</c>.</summary>
    string ResultingState { get; }

    Guid DecidedBy { get; }
    DateTime OccurredAt { get; }

    Guid? TenantId => null;


    int SchemaVersion => 1;
}
