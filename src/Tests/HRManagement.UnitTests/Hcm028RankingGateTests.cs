using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm028TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (C): ranking follows the approved committee decisions on committee-gated requisitions.</summary>
public class Hcm028RankingGateTests
{
    private const string OverrideReason = "قرار المدير بعد مراجعة ملاحظات اللجنة الموثقة";

    [Fact]
    public async Task Gate_off_keeps_current_behaviour_for_assessed_applications()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db, requiresCommitteeApproval: false);
        var candidates = await SeedCandidatesAsync(db, req, 80, 70);

        var (ok, error) = await new RecruitmentSelectionService(db).AssignRankAsync(candidates[1].Id, 1);

        Assert.True(ok, error);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.SingleAsync(a => a.Id == candidates[1].Id);
        Assert.Equal(1, saved.Rank);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, saved.Status);
        Assert.Null(saved.RankOverrideReason);
    }

    [Theory]
    [InlineData(RecruitmentApplicationStatus.InterviewScheduled)]
    [InlineData(RecruitmentApplicationStatus.Shortlisted)]
    [InlineData(RecruitmentApplicationStatus.OfferExtended)]
    public async Task Ranking_a_non_assessed_application_is_rejected(RecruitmentApplicationStatus status)
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db, requiresCommitteeApproval: false);
        var app = (await SeedCandidatesAsync(db, req, 80))[0];
        app.Status = status;
        await db.SaveChangesAsync();

        var (ok, error) = await new RecruitmentSelectionService(db).AssignRankAsync(app.Id, 1);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.NotAssessedMessage, error);
        db.ChangeTracker.Clear();
        Assert.Null((await db.RecruitmentApplications.SingleAsync()).Rank);
    }

    [Fact]
    public async Task Gate_on_without_an_approved_session_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 80, 70);
        // A submitted-but-unapproved Ranking session and an approved Interview session do not count.
        var (pending, _) = await SeedReadySessionAsync(db, req, candidates);
        Assert.True((await CreateLifecycle(db).SubmitForApprovalAsync(pending.Id, SecretaryId, "s")).Success);
        var interview = await SeedSessionAsync(db, req, CommitteePurpose.Interview);
        interview.ApprovedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var (ok, error) = await new RecruitmentSelectionService(db).AssignRankAsync(candidates[0].Id, 1);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.GateNoApprovedSessionMessage, error);
    }

    [Fact]
    public async Task Gate_on_follows_the_proposed_rank()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 70);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates);
        await ApproveAllStepsAsync(db, session.Id);

        var (ok, error) = await new RecruitmentSelectionService(db).AssignRankAsync(candidates[1].Id, 2, null, SecretaryId);

        Assert.True(ok, error);
        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == candidates[1].Id);
        Assert.Equal(2, saved.Rank);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, saved.Status);
        Assert.Null(saved.RankOverrideReason);
    }

    [Fact]
    public async Task Reject_decision_blocks_ranking()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 40);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates);
        var (decided, decisionError) = await new SelectionCommitteeService(db).UpsertDecisionAsync(
            session.Id, candidates[2].Id, CommitteeRecommendation.Reject, null, "دون الحد الأدنى", SecretaryId);
        Assert.True(decided, decisionError);
        await ApproveAllStepsAsync(db, session.Id);

        var (ok, error) = await new RecruitmentSelectionService(db).AssignRankAsync(candidates[2].Id, 3, OverrideReason);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.GateRejectedMessage, error);
    }

    [Fact]
    public async Task Rank_differing_from_proposed_needs_a_reason_which_is_stored_and_audited()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 70);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates);
        await ApproveAllStepsAsync(db, session.Id);
        var service = new RecruitmentSelectionService(db);

        var (noReason, noReasonError) = await service.AssignRankAsync(candidates[2].Id, 4, null, SecretaryId);
        var (shortReason, shortError) = await service.AssignRankAsync(candidates[2].Id, 4, "قصير", SecretaryId);
        Assert.False(noReason);
        Assert.False(shortReason);
        Assert.Equal(SelectionCommitteeRules.RankOverrideReasonMessage, noReasonError);
        Assert.Equal(SelectionCommitteeRules.RankOverrideReasonMessage, shortError);

        var (ok, error) = await service.AssignRankAsync(candidates[2].Id, 4, OverrideReason, SecretaryId);

        Assert.True(ok, error);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.SingleAsync(a => a.Id == candidates[2].Id);
        Assert.Equal(4, saved.Rank);
        Assert.Equal(OverrideReason, saved.RankOverrideReason);
        Assert.Equal(SecretaryId, saved.RankOverriddenBy);
        Assert.NotNull(saved.RankOverriddenAt);
        var audit = await db.AuditLogs.SingleAsync(a => a.EntityId == saved.Id && a.Action == "RankOverride");
        Assert.Contains("\"proposedRank\":3", audit.ChangedFields, StringComparison.Ordinal);
        Assert.Equal(SecretaryId, audit.UserId);

        // Re-ranking at the proposed rank clears the override.
        Assert.True((await service.AssignRankAsync(candidates[2].Id, 3, null, SecretaryId)).Success);
        db.ChangeTracker.Clear();
        Assert.Null((await db.RecruitmentApplications.SingleAsync(a => a.Id == candidates[2].Id)).RankOverrideReason);
    }

    [Fact]
    public async Task Bulk_apply_ranks_all_recommended_and_reserve_candidates()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 70, 30);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates.Take(3).ToList());
        // The fourth candidate is assessed after the session was formed: a decision is required before submit.
        var committee = new SelectionCommitteeService(db);
        foreach (var member in await db.SelectionCommitteeMembers.AsNoTracking().ToListAsync())
            Assert.True((await committee.RecordVoteAsync(session.Id, member.Id, candidates[3].Id, CommitteeVoteValue.Agree, null)).Success);
        Assert.True((await committee.UpsertDecisionAsync(session.Id, candidates[3].Id, CommitteeRecommendation.Reject, null, "ضعيف")).Success);
        // Ranks held before the committee (e.g. legacy data) are replaced — here swapped between 1 and 2.
        candidates[0].Rank = 2;
        candidates[0].Status = RecruitmentApplicationStatus.Ranked;
        candidates[1].Rank = 1;
        candidates[1].Status = RecruitmentApplicationStatus.Ranked;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await ApproveAllStepsAsync(db, session.Id);

        var (ok, error, count) = await new RecruitmentSelectionService(db).ApplyCommitteeRankingAsync(req.Id, SecretaryId);

        Assert.True(ok, error);
        Assert.Equal(3, count);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentApplications.AsNoTracking().ToDictionaryAsync(a => a.Id);
        Assert.Equal(1, saved[candidates[0].Id].Rank);
        Assert.Equal(2, saved[candidates[1].Id].Rank);
        Assert.Equal(3, saved[candidates[2].Id].Rank);
        Assert.Null(saved[candidates[3].Id].Rank);
        Assert.All(candidates.Take(3), c => Assert.Equal(RecruitmentApplicationStatus.Ranked, saved[c.Id].Status));
        Assert.Equal(RecruitmentApplicationStatus.Assessed, saved[candidates[3].Id].Status);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == req.Id && a.Action == "ApplyCommitteeRanking"));
    }

    [Fact]
    public async Task Bulk_apply_without_an_approved_session_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        await SeedCandidatesAsync(db, req, 90);

        var (ok, error, count) = await new RecruitmentSelectionService(db).ApplyCommitteeRankingAsync(req.Id, SecretaryId);

        Assert.False(ok);
        Assert.Equal(0, count);
        Assert.Equal(SelectionCommitteeRules.GateNoApprovedSessionMessage, error);
    }

    [Fact]
    public async Task Committee_requirement_is_frozen_after_the_first_rank()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db, requiresCommitteeApproval: false);
        var candidates = await SeedCandidatesAsync(db, req, 80);
        var service = new RecruitmentSelectionService(db);

        Assert.True((await service.SetCommitteeApprovalRequiredAsync(req.Id, true)).Success);
        Assert.True((await service.SetCommitteeApprovalRequiredAsync(req.Id, false)).Success);
        Assert.True((await service.AssignRankAsync(candidates[0].Id, 1)).Success);

        var (ok, error) = await service.SetCommitteeApprovalRequiredAsync(req.Id, true);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.RequirementFrozenMessage, error);
        db.ChangeTracker.Clear();
        Assert.False((await db.RecruitmentRequisitions.SingleAsync()).RequiresCommitteeApproval);
    }
}
