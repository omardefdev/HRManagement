using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm028TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (A3, A6): committee membership, per-candidate conflicts, votes, decisions, lock and unlock.</summary>
public class Hcm028CommitteeServiceTests
{
    [Fact]
    public async Task Duplicate_member_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var session = await SeedSessionAsync(db, req);
        var employee = await SeedEmployeeAsync(db);
        var service = new SelectionCommitteeService(db);

        Assert.True((await service.AddMemberAsync(session.Id, employee.Id, "Member", false)).Success);
        var (ok, error) = await service.AddMemberAsync(session.Id, employee.Id, "Member", false);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeService.DuplicateMemberMessage, error);
        Assert.Equal(1, await db.SelectionCommitteeMembers.CountAsync());
    }

    [Fact]
    public async Task Unknown_employee_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var session = await SeedSessionAsync(db, req);

        var (ok, error) = await new SelectionCommitteeService(db).AddMemberAsync(session.Id, Guid.NewGuid(), "Member", false);

        Assert.False(ok);
        Assert.Equal("الموظف غير موجود.", error);
    }

    [Fact]
    public async Task Conflict_on_add_recuses_member_and_chair_is_single()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var session = await SeedSessionAsync(db, req);
        var members = await AddMembersAsync(db, session, 3);
        var service = new SelectionCommitteeService(db);
        var conflicted = await SeedEmployeeAsync(db);

        Assert.True((await service.AddMemberAsync(session.Id, conflicted.Id, "Member", hasConflictOfInterest: true)).Success);
        Assert.True((await service.SetChairAsync(session.Id, members[1].Id)).Success);

        db.ChangeTracker.Clear();
        var saved = await db.SelectionCommitteeMembers.AsNoTracking().Where(m => m.SessionId == session.Id).ToListAsync();
        Assert.True(saved.Single(m => m.EmployeeId == conflicted.Id).Recused);
        Assert.Single(saved, m => SelectionCommitteeRules.IsChairRole(m.Role));
        Assert.Equal(members[1].EmployeeId, (await db.SelectionCommitteeSessions.SingleAsync()).ChairEmployeeId);

        var recusedMember = saved.Single(m => m.EmployeeId == conflicted.Id);
        var (chairOk, chairError) = await service.SetChairAsync(session.Id, recusedMember.Id);
        Assert.False(chairOk);
        Assert.Contains("منسحب", chairError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Remove_member_deletes_its_votes_and_clears_chair()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 80);
        var (session, members) = await SeedReadySessionAsync(db, req, candidates);

        var (ok, error) = await new SelectionCommitteeService(db).RemoveMemberAsync(session.Id, members[0].Id, SecretaryId);

        Assert.True(ok, error);
        db.ChangeTracker.Clear();
        Assert.Equal(2, await db.SelectionCommitteeMembers.CountAsync());
        Assert.Equal(2, await db.SelectionCommitteeVotes.CountAsync());
        Assert.Null((await db.SelectionCommitteeSessions.SingleAsync()).ChairEmployeeId);
    }

    [Fact]
    public async Task Membership_votes_and_decisions_are_locked_after_submit()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 70);
        var (session, members) = await SeedReadySessionAsync(db, req, candidates);

        var (submitted, submitError) = await CreateLifecycle(db).SubmitForApprovalAsync(session.Id, SecretaryId, "secretary");
        Assert.True(submitted, submitError);

        var service = new SelectionCommitteeService(db);
        var extra = await SeedEmployeeAsync(db);
        var app = candidates[0];
        var results = new[]
        {
            await service.AddMemberAsync(session.Id, extra.Id, "Member", false),
            await service.RemoveMemberAsync(session.Id, members[2].Id),
            await service.SetChairAsync(session.Id, members[1].Id),
            await service.RecordMemberCoiAsync(session.Id, members[1].Id, app.Id, true),
            await service.RecordVoteAsync(session.Id, members[1].Id, app.Id, CommitteeVoteValue.Disagree, null),
            await service.UpsertDecisionAsync(session.Id, app.Id, CommitteeRecommendation.Reject, null, null)
        };

        Assert.All(results, r =>
        {
            Assert.False(r.Success);
            Assert.Equal(SelectionCommitteeRules.LockedMessage, r.Error);
        });

        db.ChangeTracker.Clear();
        var saved = await db.SelectionCommitteeSessions.SingleAsync();
        Assert.NotNull(saved.SubmittedAt);
        Assert.NotNull(saved.LockedAt);
        Assert.NotNull(saved.WorkflowInstanceId);
        Assert.Equal(3, await db.SelectionCommitteeMembers.CountAsync());
    }

    [Fact]
    public async Task Submit_with_unmet_requirements_is_rejected_and_stays_unlocked()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90);
        var session = await SeedSessionAsync(db, req);
        await AddMembersAsync(db, session, 3);

        var (ok, error) = await CreateLifecycle(db).SubmitForApprovalAsync(session.Id, SecretaryId, "secretary");

        Assert.False(ok);
        Assert.Contains("لا توجد توصية", error, StringComparison.Ordinal);
        Assert.Contains(candidates[0].ApplicantFullName, error, StringComparison.Ordinal);
        db.ChangeTracker.Clear();
        var saved = await db.SelectionCommitteeSessions.SingleAsync();
        Assert.Null(saved.LockedAt);
        Assert.Null(saved.WorkflowInstanceId);
    }

    [Fact]
    public async Task Vote_by_recused_member_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 80);
        var session = await SeedSessionAsync(db, req);
        var employee = await SeedEmployeeAsync(db);
        var service = new SelectionCommitteeService(db);
        Assert.True((await service.AddMemberAsync(session.Id, employee.Id, "Member", hasConflictOfInterest: true)).Success);
        var member = await db.SelectionCommitteeMembers.SingleAsync();

        var (ok, error) = await service.RecordVoteAsync(
            session.Id, member.Id, candidates[0].Id, CommitteeVoteValue.Agree, null, SecretaryId);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.RecusedVoterMessage, error);
    }

    [Fact]
    public async Task Conflicted_member_cannot_vote_on_that_candidate_but_can_on_others()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 80, 70);
        var session = await SeedSessionAsync(db, req);
        var members = await AddMembersAsync(db, session, 3);
        var service = new SelectionCommitteeService(db);

        Assert.True((await service.RecordVoteAsync(
            session.Id, members[2].Id, candidates[0].Id, CommitteeVoteValue.Agree, null, SecretaryId)).Success);
        Assert.True((await service.RecordMemberCoiAsync(
            session.Id, members[2].Id, candidates[0].Id, hasConflict: true, SecretaryId)).Success);

        // The conflict withdraws the earlier vote on that candidate.
        Assert.False(await db.SelectionCommitteeVotes.AnyAsync(v => v.MemberId == members[2].Id && v.ApplicationId == candidates[0].Id));

        var (blocked, error) = await service.RecordVoteAsync(
            session.Id, members[2].Id, candidates[0].Id, CommitteeVoteValue.Agree, null, SecretaryId);
        Assert.False(blocked);
        Assert.Equal(SelectionCommitteeRules.ConflictedVoterMessage, error);

        var (allowed, otherError) = await service.RecordVoteAsync(
            session.Id, members[2].Id, candidates[1].Id, CommitteeVoteValue.Disagree, "ملاحظة", SecretaryId);
        Assert.True(allowed, otherError);

        var vote = await db.SelectionCommitteeVotes.AsNoTracking().SingleAsync(v => v.ApplicationId == candidates[1].Id);
        Assert.Equal(SecretaryId, vote.RecordedBy);
        Assert.Equal(CommitteeVoteValue.Disagree, vote.Vote);

        var workspace = await service.GetWorkspaceAsync(session.Id);
        Assert.Equal(2, workspace!.TallyFor(candidates[0].Id).EligibleVoters);
        Assert.Equal(3, workspace.TallyFor(candidates[1].Id).EligibleVoters);
    }

    [Fact]
    public async Task Votes_and_decisions_only_for_assessed_candidates_of_the_requisition()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 80);
        var notAssessed = (await SeedCandidatesAsync(db, req, 50))[0];
        notAssessed.Status = RecruitmentApplicationStatus.InterviewScheduled;
        await db.SaveChangesAsync();
        var session = await SeedSessionAsync(db, req);
        var members = await AddMembersAsync(db, session, 3);
        var service = new SelectionCommitteeService(db);

        var vote = await service.RecordVoteAsync(session.Id, members[0].Id, notAssessed.Id, CommitteeVoteValue.Agree, null);
        var decision = await service.UpsertDecisionAsync(session.Id, notAssessed.Id, CommitteeRecommendation.Recommend, 1, null);
        var rejectWithRank = await service.UpsertDecisionAsync(session.Id, candidates[0].Id, CommitteeRecommendation.Reject, 1, null);

        Assert.Equal(SelectionCommitteeService.NotCandidateMessage, vote.Error);
        Assert.Equal(SelectionCommitteeService.NotCandidateMessage, decision.Error);
        Assert.False(rejectWithRank.Success);

        var workspace = await service.GetWorkspaceAsync(session.Id);
        Assert.Equal([candidates[0].Id], workspace!.Candidates.Select(c => c.Id));
    }

    [Fact]
    public async Task Workflow_rejection_unlocks_the_session_for_rework()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 70);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates);
        var lifecycle = CreateLifecycle(db);
        Assert.True((await lifecycle.SubmitForApprovalAsync(session.Id, SecretaryId, "secretary")).Success);

        var (rejected, rejectError) = await CreateLifecycle(db)
            .RejectStepAsync(session.Id, Actor(Step1ApproverId), null, "يرجى استكمال التعليل");

        Assert.True(rejected, rejectError);
        db.ChangeTracker.Clear();
        var saved = await db.SelectionCommitteeSessions.SingleAsync();
        Assert.Null(saved.WorkflowInstanceId);
        Assert.Null(saved.SubmittedAt);
        Assert.Null(saved.LockedAt);
        Assert.Null(saved.ApprovedAt);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityId == session.Id && a.Action == "CommitteeWorkflowRejected"));

        var extra = await SeedEmployeeAsync(db);
        var (added, addError) = await new SelectionCommitteeService(db).AddMemberAsync(session.Id, extra.Id, "Member", false);
        Assert.True(added, addError);
        Assert.True((await CreateLifecycle(db).SubmitForApprovalAsync(session.Id, SecretaryId, "secretary")).Success);
    }

    [Fact]
    public async Task Committee_member_queries_cover_all_sessions_of_the_requisition()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var session = await SeedSessionAsync(db, req);
        var members = await AddMembersAsync(db, session, 2);
        var service = new SelectionCommitteeService(db);
        var outsider = await SeedEmployeeAsync(db);

        var ids = await service.GetMemberEmployeeIdsAsync(req.Id);

        Assert.Equal(members.Select(m => m.EmployeeId).OrderBy(x => x), ids.OrderBy(x => x));
        Assert.True(await service.IsCommitteeMemberAsync(req.Id, members[0].EmployeeId));
        Assert.False(await service.IsCommitteeMemberAsync(req.Id, outsider.Id));
    }
}
