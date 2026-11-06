namespace HRManagement.ServiceDefaults.Workflow.CrossService;

/// <summary>
/// PLAN-43 (G-WF-05): the wire body of an approval start, in the names and order PLAN-60 section 0.1 relies on. It never carries the
/// requester: the server derives that from the owning service's X-Acting-User. PLAN-46 adds the client, the event and the status
/// around it in this folder.
/// </summary>
public sealed record WorkflowApprovalStartRequest(
    string EntityType,
    Guid EntityId,
    string WorkflowCode,
    string IdempotencyKey,
    IReadOnlyCollection<Guid> ExcludedApproverUserIds,
    Guid? OrgUnitId,
    string DisplayTitle,
    string DetailsUrl,
    IReadOnlyDictionary<string, string>? Facts,
    Guid? SubjectEmployeeId = null);

/// <summary>What a start answers: the instance, and its state even when it finished long ago.</summary>
public sealed record WorkflowApprovalStartResponse(Guid Id, string Status, bool AlreadyActive);

public sealed record WorkflowApprovalCancelRequest(string Reason);
