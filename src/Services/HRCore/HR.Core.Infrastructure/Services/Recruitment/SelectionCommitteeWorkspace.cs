using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;

namespace HR.Core.Infrastructure.Services.Recruitment;

/// <summary>HCM-028: a committee session with its members, per-candidate conflicts, votes, decisions and candidates.</summary>
public sealed record SelectionCommitteeWorkspace(
    SelectionCommitteeSession Session,
    IReadOnlyList<SelectionCommitteeMember> Members,
    IReadOnlyList<SelectionCommitteeMemberCoi> CoiDeclarations,
    IReadOnlyList<SelectionCommitteeVote> Votes,
    IReadOnlyList<SelectionCommitteeDecision> Decisions,
    IReadOnlyList<RecruitmentApplication> Candidates)
{
    public IReadOnlyList<string> SubmitIssues =>
        SelectionCommitteeRules.GetSubmitIssues(Members, CoiDeclarations, Votes, Decisions, Candidates);

    /// <summary>Proposed rank pre-fill: score descending, earlier application first.</summary>
    public IReadOnlyDictionary<Guid, int> SuggestedRanks => SelectionCommitteeRules.SuggestedRanks(Candidates);

    public CommitteeVoteTally TallyFor(Guid applicationId) =>
        SelectionCommitteeRules.Tally(Members, CoiDeclarations, Votes, applicationId);

    public bool HasConflict(Guid memberId, Guid applicationId) =>
        SelectionCommitteeRules.HasConflictFor(CoiDeclarations, memberId, applicationId);

    public SelectionCommitteeVote? VoteOf(Guid memberId, Guid applicationId) =>
        Votes.FirstOrDefault(v => v.MemberId == memberId && v.ApplicationId == applicationId);

    public SelectionCommitteeDecision? DecisionFor(Guid applicationId) =>
        Decisions.FirstOrDefault(d => d.ApplicationId == applicationId);
}
