using HR.Core.Domain.Entities;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>
/// HCM-028 (C): the committee gate on ranking for one requisition — <see cref="Required"/> mirrors
/// <c>RecruitmentRequisition.RequiresCommitteeApproval</c>; <see cref="ApprovedSession"/> is the latest approved
/// Ranking/Final session (decisions included), or null.
/// </summary>
public sealed record CommitteeRankingGate(bool Required, SelectionCommitteeSession? ApprovedSession)
{
    public SelectionCommitteeDecision? DecisionFor(Guid applicationId) =>
        ApprovedSession?.Decisions.FirstOrDefault(d => d.ApplicationId == applicationId);
}
