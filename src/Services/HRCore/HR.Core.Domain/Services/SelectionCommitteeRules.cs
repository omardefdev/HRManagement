using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;

namespace HR.Core.Domain.Services;

/// <summary>
/// HCM-028 (G-10): the selection committee as an audited decision body — eligible voters, majority,
/// submit readiness, approval separation of duties and the ranking gate.
/// </summary>
public static class SelectionCommitteeRules
{
    public const int MinimumQuorum = 3;
    public const int QuorumStepLevel = 2;
    public const int FinalApprovalStepLevel = 3;
    public const int MinRankOverrideReasonLength = 20;

    public const string LockedMessage = "تم تقديم الجلسة للاعتماد — العضوية مقفلة.";
    public const string QuorumMessage = "النصاب غير مكتمل: لا يمكن اعتماد خطوة النصاب دون 3 أعضاء فعّالين على الأقل (غير منسحبين).";
    public const string ApproverIsMemberOrCreatorMessage = "لا يجوز لعضو في اللجنة أو منشئها اعتماد الجلسة.";
    public const string QuorumNotMetMessage = "النصاب غير مكتمل: يجب 3 أعضاء على الأقل غير منسحبين.";
    public const string ChairRequiredMessage = "يجب تعيين رئيس واحد للجنة من الأعضاء غير المنسحبين.";
    public const string NoCandidatesMessage = "لا يوجد مرشحون مُقيَّمون على طلب الشاغر.";
    public const string RecusedVoterMessage = "العضو منسحب من اللجنة ولا يحق له التصويت.";
    public const string ConflictedVoterMessage = "العضو مسجّل بتعارض مصالح مع هذا المرشح ولا يحق له التصويت عليه.";
    public const string NotAssessedMessage = "يمكن الترتيب للطلبات المُقيَّمة فقط.";
    public const string GateNoApprovedSessionMessage = "يتطلب الترتيب اعتماد لجنة الاختيار أولاً.";
    public const string GateRejectedMessage = "أوصت اللجنة برفض هذا المرشح.";
    public const string GateNoDecisionMessage = "لا توجد توصية للجنة بشأن هذا المرشح في الجلسة المعتمدة.";
    public const string RequirementFrozenMessage = "لا يمكن تغيير إلزامية اللجنة بعد بدء الترتيب.";

    public static readonly string RankOverrideReasonMessage =
        $"الترتيب مخالف للترتيب المقترح من اللجنة؛ يلزم سبب لا يقل عن {MinRankOverrideReasonLength} حرفاً.";

    /// <summary>Applications the committee must decide on: Assessed or Ranked on the requisition.</summary>
    public static readonly IReadOnlySet<RecruitmentApplicationStatus> CandidateStatuses =
        new HashSet<RecruitmentApplicationStatus>
        {
            RecruitmentApplicationStatus.Assessed,
            RecruitmentApplicationStatus.Ranked
        };

    /// <summary>Only these purposes can satisfy the ranking gate.</summary>
    public static readonly IReadOnlySet<CommitteePurpose> RankingPurposes =
        new HashSet<CommitteePurpose> { CommitteePurpose.Ranking, CommitteePurpose.Final };

    public static bool IsChairRole(string? role) =>
        string.Equals(role, SelectionCommitteeMember.ChairRole, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role?.Trim(), "رئيس", StringComparison.Ordinal);

    public static bool HasQuorum(IEnumerable<SelectionCommitteeMember> members) =>
        RecruitmentAppealWorkflowRules.HasQuorum(members, MinimumQuorum);

    public static bool HasConflictFor(
        IEnumerable<SelectionCommitteeMemberCoi> coiDeclarations,
        Guid memberId,
        Guid applicationId) =>
        coiDeclarations.Any(c => c.MemberId == memberId && c.ApplicationId == applicationId && c.HasConflict);

    /// <summary>Non-recused members without a conflict declared for this application.</summary>
    public static IReadOnlyList<SelectionCommitteeMember> EligibleVoters(
        IEnumerable<SelectionCommitteeMember> members,
        IEnumerable<SelectionCommitteeMemberCoi> coiDeclarations,
        Guid applicationId)
    {
        var coi = coiDeclarations as IReadOnlyCollection<SelectionCommitteeMemberCoi> ?? coiDeclarations.ToList();
        return members
            .Where(m => !m.Recused && !HasConflictFor(coi, m.Id, applicationId))
            .ToList();
    }

    /// <summary>Null when the member may vote on the application.</summary>
    public static string? ValidateVoter(
        SelectionCommitteeMember member,
        IEnumerable<SelectionCommitteeMemberCoi> coiDeclarations,
        Guid applicationId)
    {
        if (member.Recused)
            return RecusedVoterMessage;
        if (HasConflictFor(coiDeclarations, member.Id, applicationId))
            return ConflictedVoterMessage;
        return null;
    }

    /// <summary>Vote totals for one application, counting eligible voters only.</summary>
    public static CommitteeVoteTally Tally(
        IEnumerable<SelectionCommitteeMember> members,
        IEnumerable<SelectionCommitteeMemberCoi> coiDeclarations,
        IEnumerable<SelectionCommitteeVote> votes,
        Guid applicationId)
    {
        var eligible = EligibleVoters(members, coiDeclarations, applicationId);
        var eligibleIds = eligible.Select(m => m.Id).ToHashSet();
        var counted = votes
            .Where(v => v.ApplicationId == applicationId && eligibleIds.Contains(v.MemberId))
            .ToList();
        return new CommitteeVoteTally(
            eligible.Count,
            counted.Count(v => v.Vote == CommitteeVoteValue.Agree),
            counted.Count(v => v.Vote == CommitteeVoteValue.Disagree),
            counted.Count(v => v.Vote == CommitteeVoteValue.Abstain));
    }

    /// <summary>Strict majority of eligible voters agree; abstentions and missing votes count as not-Agree.</summary>
    public static bool IsSupported(CommitteeVoteTally tally) =>
        tally.EligibleVoters > 0 && tally.Agree * 2 > tally.EligibleVoters;

    /// <summary>Candidates ordered by computed score (desc), ties broken by earlier application date.</summary>
    public static IReadOnlyList<RecruitmentApplication> SuggestRanking(IEnumerable<RecruitmentApplication> candidates) =>
        candidates
            .OrderByDescending(a => a.AssessmentScore.HasValue)
            .ThenByDescending(a => a.AssessmentScore)
            .ThenBy(a => a.AppliedAt)
            .ThenBy(a => a.Id)
            .ToList();

    /// <summary>Suggested proposed rank (1-based) per application id.</summary>
    public static IReadOnlyDictionary<Guid, int> SuggestedRanks(IEnumerable<RecruitmentApplication> candidates) =>
        SuggestRanking(candidates)
            .Select((a, i) => (a.Id, Rank: i + 1))
            .ToDictionary(x => x.Id, x => x.Rank);

    /// <summary>Every unmet submit requirement (empty when the session can be submitted).</summary>
    public static IReadOnlyList<string> GetSubmitIssues(
        IReadOnlyCollection<SelectionCommitteeMember> members,
        IReadOnlyCollection<SelectionCommitteeMemberCoi> coiDeclarations,
        IReadOnlyCollection<SelectionCommitteeVote> votes,
        IReadOnlyCollection<SelectionCommitteeDecision> decisions,
        IReadOnlyCollection<RecruitmentApplication> candidates)
    {
        var issues = new List<string>();

        if (!HasQuorum(members))
            issues.Add(QuorumNotMetMessage);

        var chairs = members.Where(m => IsChairRole(m.Role)).ToList();
        if (chairs.Count != 1 || chairs[0].Recused)
            issues.Add(ChairRequiredMessage);

        if (candidates.Count == 0)
        {
            issues.Add(NoCandidatesMessage);
            return issues;
        }

        var byApplication = decisions.ToDictionary(d => d.ApplicationId);
        var rankedDecisions = new List<(RecruitmentApplication App, SelectionCommitteeDecision Decision)>();

        foreach (var candidate in candidates.OrderBy(c => c.ApplicantFullName, StringComparer.Ordinal))
        {
            if (!byApplication.TryGetValue(candidate.Id, out var decision))
            {
                issues.Add($"لا توجد توصية للمرشح «{candidate.ApplicantFullName}».");
                continue;
            }

            var tally = Tally(members, coiDeclarations, votes, candidate.Id);
            if (!IsSupported(tally))
            {
                issues.Add(
                    $"توصية المرشح «{candidate.ApplicantFullName}» غير مدعومة بأغلبية الأعضاء المؤهلين " +
                    $"({tally.Agree} موافق من {tally.EligibleVoters}).");
            }

            if (decision.Recommendation == CommitteeRecommendation.Reject)
            {
                if (decision.ProposedRank is not null)
                    issues.Add($"التوصية برفض المرشح «{candidate.ApplicantFullName}» لا تحمل ترتيباً مقترحاً.");
            }
            else if (decision.ProposedRank is not > 0)
            {
                issues.Add($"يجب إدخال ترتيب مقترح موجب للمرشح «{candidate.ApplicantFullName}».");
            }
            else
            {
                rankedDecisions.Add((candidate, decision));
            }
        }

        foreach (var group in rankedDecisions.GroupBy(x => x.Decision.ProposedRank).Where(g => g.Count() > 1))
        {
            var names = string.Join("، ", group.Select(x => x.App.ApplicantFullName));
            issues.Add($"الترتيب المقترح {group.Key} مكرر ({names}).");
        }

        return issues;
    }

    /// <summary>First unmet submit requirement, or null.</summary>
    public static string? ValidateForSubmit(
        IReadOnlyCollection<SelectionCommitteeMember> members,
        IReadOnlyCollection<SelectionCommitteeMemberCoi> coiDeclarations,
        IReadOnlyCollection<SelectionCommitteeVote> votes,
        IReadOnlyCollection<SelectionCommitteeDecision> decisions,
        IReadOnlyCollection<RecruitmentApplication> candidates) =>
        GetSubmitIssues(members, coiDeclarations, votes, decisions, candidates).FirstOrDefault();

    /// <summary>
    /// Approval separation of duties for the committee workflow: the session creator never approves; from the
    /// quorum step on the quorum must hold; the final (HR director) step cannot be approved by a member.
    /// <paramref name="members"/> null skips the member and quorum checks (caller only knows the user id).
    /// </summary>
    public static string? ValidateApproval(
        int currentStepLevel,
        IEnumerable<SelectionCommitteeMember>? members,
        Guid? sessionCreatedBy,
        Guid? actorUserId,
        Guid? actorEmployeeId)
    {
        if (actorUserId.HasValue && sessionCreatedBy == actorUserId)
            return ApproverIsMemberOrCreatorMessage;

        if (members is null)
            return null;

        var list = members as IReadOnlyCollection<SelectionCommitteeMember> ?? members.ToList();
        if (currentStepLevel >= QuorumStepLevel && !HasQuorum(list))
            return QuorumMessage;

        if (currentStepLevel >= FinalApprovalStepLevel
            && actorEmployeeId is Guid employeeId
            && list.Any(m => m.EmployeeId == employeeId))
            return ApproverIsMemberOrCreatorMessage;

        return null;
    }

    /// <summary>Ranking-gate check of one rank against the approved committee decision.</summary>
    public static string? ValidateRankAgainstDecision(
        SelectionCommitteeDecision? decision,
        int rank,
        string? rankOverrideReason)
    {
        if (decision is null)
            return GateNoDecisionMessage;
        if (decision.Recommendation == CommitteeRecommendation.Reject)
            return GateRejectedMessage;
        if (decision.ProposedRank == rank)
            return null;
        return IsValidOverrideReason(rankOverrideReason) ? null : RankOverrideReasonMessage;
    }

    public static bool IsValidOverrideReason(string? reason) =>
        reason is not null && reason.Trim().Length >= MinRankOverrideReasonLength;
}

/// <summary>HCM-028: committee vote totals for one candidate.</summary>
public sealed record CommitteeVoteTally(int EligibleVoters, int Agree, int Disagree, int Abstain);
