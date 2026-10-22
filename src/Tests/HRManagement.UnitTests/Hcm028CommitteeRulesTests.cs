using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (A5): pure committee rules — quorum, chair, majority, per-candidate conflicts, submit, ranking.</summary>
public class Hcm028CommitteeRulesTests
{
    private static SelectionCommitteeMember Member(bool chair = false, bool recused = false) => new()
    {
        EmployeeId = Guid.NewGuid(),
        Role = chair ? SelectionCommitteeMember.ChairRole : SelectionCommitteeMember.MemberRole,
        Recused = recused,
        HasConflictOfInterest = recused
    };

    private static RecruitmentApplication Candidate(string name, decimal? score, int appliedDaysAgo = 1) => new()
    {
        ApplicantFullName = name,
        AppliedPosition = "محلل",
        AssessmentScore = score,
        AppliedAt = DateTime.UtcNow.AddDays(-appliedDaysAgo),
        Status = RecruitmentApplicationStatus.Assessed
    };

    private static SelectionCommitteeVote Vote(SelectionCommitteeMember m, RecruitmentApplication a, CommitteeVoteValue v) =>
        new() { MemberId = m.Id, ApplicationId = a.Id, Vote = v };

    private static SelectionCommitteeDecision Decision(
        RecruitmentApplication a,
        CommitteeRecommendation r = CommitteeRecommendation.Recommend,
        int? rank = 1) =>
        new() { ApplicationId = a.Id, Recommendation = r, ProposedRank = rank };

    private static List<SelectionCommitteeVote> AllAgree(IEnumerable<SelectionCommitteeMember> members, RecruitmentApplication a) =>
        members.Where(m => !m.Recused).Select(m => Vote(m, a, CommitteeVoteValue.Agree)).ToList();

    [Fact]
    public void Quorum_needs_three_non_recused_members()
    {
        Assert.False(SelectionCommitteeRules.HasQuorum([Member(), Member(), Member(recused: true)]));
        Assert.True(SelectionCommitteeRules.HasQuorum([Member(), Member(), Member(), Member(recused: true)]));
    }

    [Fact]
    public void Submit_requires_quorum()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member() };
        var app = Candidate("أ", 80);
        var issues = SelectionCommitteeRules.GetSubmitIssues(members, [], AllAgree(members, app), [Decision(app)], [app]);

        Assert.Contains(SelectionCommitteeRules.QuorumNotMetMessage, issues);
    }

    [Fact]
    public void Submit_requires_exactly_one_non_recused_chair()
    {
        var app = Candidate("أ", 80);
        var noChair = new List<SelectionCommitteeMember> { Member(), Member(), Member() };
        var twoChairs = new List<SelectionCommitteeMember> { Member(chair: true), Member(chair: true), Member() };
        var recusedChair = new List<SelectionCommitteeMember> { Member(chair: true, recused: true), Member(), Member(), Member() };

        foreach (var members in new[] { noChair, twoChairs, recusedChair })
        {
            var issues = SelectionCommitteeRules.GetSubmitIssues(members, [], AllAgree(members, app), [Decision(app)], [app]);
            Assert.Contains(SelectionCommitteeRules.ChairRequiredMessage, issues);
        }
    }

    [Fact]
    public void Valid_session_has_no_submit_issues()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var a = Candidate("أ", 90);
        var b = Candidate("ب", 80);
        var c = Candidate("ج", 50);
        var votes = AllAgree(members, a).Concat(AllAgree(members, b)).Concat(AllAgree(members, c)).ToList();
        var decisions = new List<SelectionCommitteeDecision>
        {
            Decision(a, rank: 1),
            Decision(b, CommitteeRecommendation.Reserve, 2),
            Decision(c, CommitteeRecommendation.Reject, null)
        };

        Assert.Empty(SelectionCommitteeRules.GetSubmitIssues(members, [], votes, decisions, [a, b, c]));
        Assert.Null(SelectionCommitteeRules.ValidateForSubmit(members, [], votes, decisions, [a, b, c]));
    }

    [Fact]
    public void Majority_is_strict_and_abstentions_count_as_not_agree()
    {
        var m1 = Member(chair: true);
        var m2 = Member();
        var m3 = Member();
        var m4 = Member();
        var members = new List<SelectionCommitteeMember> { m1, m2, m3, m4 };
        var app = Candidate("أ", 80);

        // 2 of 4 agree, 2 abstain → not a strict majority.
        var halfVotes = new List<SelectionCommitteeVote>
        {
            Vote(m1, app, CommitteeVoteValue.Agree),
            Vote(m2, app, CommitteeVoteValue.Agree),
            Vote(m3, app, CommitteeVoteValue.Abstain),
            Vote(m4, app, CommitteeVoteValue.Abstain)
        };
        var tally = SelectionCommitteeRules.Tally(members, [], halfVotes, app.Id);
        Assert.Equal(new CommitteeVoteTally(4, 2, 0, 2), tally);
        Assert.False(SelectionCommitteeRules.IsSupported(tally));

        halfVotes[2] = Vote(m3, app, CommitteeVoteValue.Agree);
        Assert.True(SelectionCommitteeRules.IsSupported(SelectionCommitteeRules.Tally(members, [], halfVotes, app.Id)));
    }

    [Fact]
    public void Missing_votes_count_as_not_agree()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var app = Candidate("أ", 80);
        var oneVote = new List<SelectionCommitteeVote> { Vote(members[0], app, CommitteeVoteValue.Agree) };

        var issues = SelectionCommitteeRules.GetSubmitIssues(members, [], oneVote, [Decision(app)], [app]);
        Assert.Contains(issues, i => i.Contains("غير مدعومة", StringComparison.Ordinal));
    }

    [Fact]
    public void Conflicted_member_is_not_an_eligible_voter_for_that_candidate_only()
    {
        var m1 = Member(chair: true);
        var m2 = Member();
        var m3 = Member();
        var members = new List<SelectionCommitteeMember> { m1, m2, m3 };
        var a = Candidate("أ", 80);
        var b = Candidate("ب", 70);
        var coi = new List<SelectionCommitteeMemberCoi>
        {
            new() { MemberId = m3.Id, ApplicationId = a.Id, HasConflict = true }
        };

        Assert.Equal(2, SelectionCommitteeRules.EligibleVoters(members, coi, a.Id).Count);
        Assert.Equal(3, SelectionCommitteeRules.EligibleVoters(members, coi, b.Id).Count);
        Assert.Equal(SelectionCommitteeRules.ConflictedVoterMessage, SelectionCommitteeRules.ValidateVoter(m3, coi, a.Id));
        Assert.Null(SelectionCommitteeRules.ValidateVoter(m3, coi, b.Id));

        // A stale vote of the conflicted member is not counted: 2 eligible, 1 agree → not supported.
        var votes = new List<SelectionCommitteeVote>
        {
            Vote(m1, a, CommitteeVoteValue.Agree),
            Vote(m2, a, CommitteeVoteValue.Disagree),
            Vote(m3, a, CommitteeVoteValue.Agree)
        };
        var tally = SelectionCommitteeRules.Tally(members, coi, votes, a.Id);
        Assert.Equal(new CommitteeVoteTally(2, 1, 1, 0), tally);
        Assert.False(SelectionCommitteeRules.IsSupported(tally));
    }

    [Fact]
    public void Recused_member_cannot_vote()
    {
        var recused = Member(recused: true);
        Assert.Equal(SelectionCommitteeRules.RecusedVoterMessage,
            SelectionCommitteeRules.ValidateVoter(recused, [], Guid.NewGuid()));
    }

    [Fact]
    public void Submit_requires_a_decision_for_every_candidate()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var a = Candidate("أ", 80);
        var b = Candidate("ب", 70);
        var votes = AllAgree(members, a).Concat(AllAgree(members, b)).ToList();

        var issues = SelectionCommitteeRules.GetSubmitIssues(members, [], votes, [Decision(a)], [a, b]);
        Assert.Contains(issues, i => i.Contains("لا توجد توصية", StringComparison.Ordinal) && i.Contains("ب", StringComparison.Ordinal));
    }

    [Fact]
    public void Submit_without_candidates_is_rejected()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        Assert.Contains(SelectionCommitteeRules.NoCandidatesMessage,
            SelectionCommitteeRules.GetSubmitIssues(members, [], [], [], []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Recommend_and_reserve_need_a_positive_proposed_rank(int? rank)
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var a = Candidate("أ", 80);
        var b = Candidate("ب", 70);
        var votes = AllAgree(members, a).Concat(AllAgree(members, b)).ToList();
        var decisions = new List<SelectionCommitteeDecision>
        {
            Decision(a, rank: 1),
            Decision(b, CommitteeRecommendation.Reserve, rank)
        };

        var issues = SelectionCommitteeRules.GetSubmitIssues(members, [], votes, decisions, [a, b]);
        Assert.Contains(issues, i => i.Contains("ترتيب مقترح موجب", StringComparison.Ordinal));
    }

    [Fact]
    public void Duplicate_proposed_rank_is_rejected()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var a = Candidate("أ", 80);
        var b = Candidate("ب", 70);
        var votes = AllAgree(members, a).Concat(AllAgree(members, b)).ToList();
        var decisions = new List<SelectionCommitteeDecision>
        {
            Decision(a, rank: 1),
            Decision(b, CommitteeRecommendation.Reserve, 1)
        };

        var issues = SelectionCommitteeRules.GetSubmitIssues(members, [], votes, decisions, [a, b]);
        Assert.Contains(issues, i => i.Contains("مكرر", StringComparison.Ordinal));
    }

    [Fact]
    public void Reject_with_a_proposed_rank_is_invalid()
    {
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var a = Candidate("أ", 80);

        var issues = SelectionCommitteeRules.GetSubmitIssues(
            members, [], AllAgree(members, a), [Decision(a, CommitteeRecommendation.Reject, 1)], [a]);
        Assert.Contains(issues, i => i.Contains("لا تحمل ترتيباً", StringComparison.Ordinal));
    }

    [Fact]
    public void Suggested_ranking_orders_by_score_then_earlier_application()
    {
        var low = Candidate("منخفض", 60, appliedDaysAgo: 10);
        var tieLate = Candidate("متأخر", 85, appliedDaysAgo: 2);
        var tieEarly = Candidate("مبكر", 85, appliedDaysAgo: 5);
        var top = Candidate("أعلى", 95, appliedDaysAgo: 1);
        var unscored = Candidate("بلا درجة", null, appliedDaysAgo: 30);

        var order = SelectionCommitteeRules.SuggestRanking([low, tieLate, unscored, tieEarly, top]);
        Assert.Equal(new[] { top, tieEarly, tieLate, low, unscored }, order);

        var ranks = SelectionCommitteeRules.SuggestedRanks([low, tieLate, tieEarly, top]);
        Assert.Equal(1, ranks[top.Id]);
        Assert.Equal(2, ranks[tieEarly.Id]);
        Assert.Equal(3, ranks[tieLate.Id]);
        Assert.Equal(4, ranks[low.Id]);
    }

    [Fact]
    public void Approval_blocks_creator_quorum_and_members_on_final_step()
    {
        var creator = Guid.NewGuid();
        var members = new List<SelectionCommitteeMember> { Member(chair: true), Member(), Member() };
        var memberEmployee = members[1].EmployeeId;

        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
            SelectionCommitteeRules.ValidateApproval(1, members, creator, creator, null));
        Assert.Null(SelectionCommitteeRules.ValidateApproval(1, members, creator, Guid.NewGuid(), memberEmployee));
        Assert.Equal(SelectionCommitteeRules.QuorumMessage,
            SelectionCommitteeRules.ValidateApproval(2, members.Take(2), creator, Guid.NewGuid(), null));
        Assert.Null(SelectionCommitteeRules.ValidateApproval(2, members, creator, Guid.NewGuid(), memberEmployee));
        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
            SelectionCommitteeRules.ValidateApproval(3, members, creator, Guid.NewGuid(), memberEmployee));
        Assert.Null(SelectionCommitteeRules.ValidateApproval(3, members, creator, Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Rank_against_decision_requires_match_or_reason()
    {
        var decision = new SelectionCommitteeDecision { Recommendation = CommitteeRecommendation.Recommend, ProposedRank = 2 };

        Assert.Null(SelectionCommitteeRules.ValidateRankAgainstDecision(decision, 2, null));
        Assert.Equal(SelectionCommitteeRules.RankOverrideReasonMessage,
            SelectionCommitteeRules.ValidateRankAgainstDecision(decision, 1, "قصير"));
        Assert.Null(SelectionCommitteeRules.ValidateRankAgainstDecision(decision, 1, "سبب التجاوز موثق في محضر الاجتماع"));
        Assert.Equal(SelectionCommitteeRules.GateRejectedMessage,
            SelectionCommitteeRules.ValidateRankAgainstDecision(
                new SelectionCommitteeDecision { Recommendation = CommitteeRecommendation.Reject }, 1, null));
        Assert.Equal(SelectionCommitteeRules.GateNoDecisionMessage,
            SelectionCommitteeRules.ValidateRankAgainstDecision(null, 1, null));
    }
}
