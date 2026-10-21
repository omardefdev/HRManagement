using HR.Core.Domain.Entities;

namespace HR.Core.Web.Services;

/// <summary>Entity-specific gates for inbox eligibility (mirrors Details workflow panels).</summary>
public sealed class WorkflowInboxEntityContext
{
    /// <summary>Org unit used for branch inbox filtering (position/requisition scope).</summary>
    public Guid? ScopeOrgUnitId { get; init; }

    public Guid? EntityCreatedBy { get; init; }

    public IReadOnlyList<SelectionCommitteeMember>? CommitteeMembers { get; init; }

    public RecruitmentAppeal? Appeal { get; init; }

    public bool IsAwaitingApproval { get; init; } = true;

    public string? CreatorSoDMessage { get; init; }

    public bool BlockInitiatorOnLaterSteps { get; init; } = true;
}
