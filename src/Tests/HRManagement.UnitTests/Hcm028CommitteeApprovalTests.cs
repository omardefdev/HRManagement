using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm028TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-028 (B): server-side quorum and separation of duties on the 3-step committee workflow.</summary>
public class Hcm028CommitteeApprovalTests
{
    private static async Task<(Guid SessionId, List<HR.Core.Domain.Entities.SelectionCommitteeMember> Members)> SubmitReadySessionAsync(
        HR.Core.Infrastructure.Data.HRCoreDbContext db)
    {
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 70);
        var (session, members) = await SeedReadySessionAsync(db, req, candidates);
        var (ok, error) = await CreateLifecycle(db).SubmitForApprovalAsync(session.Id, SecretaryId, "secretary");
        Assert.True(ok, error);
        return (session.Id, members);
    }

    private static async Task<int> CurrentStepAsync(HR.Core.Infrastructure.Data.HRCoreDbContext db, Guid sessionId)
    {
        var workflowId = await db.SelectionCommitteeSessions.AsNoTracking()
            .Where(s => s.Id == sessionId).Select(s => s.WorkflowInstanceId).SingleAsync();
        return await db.WorkflowInstances.AsNoTracking()
            .Where(i => i.Id == workflowId).Select(i => i.CurrentStepLevel).SingleAsync();
    }

    [Fact]
    public async Task Step2_approval_without_quorum_is_rejected_server_side()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (sessionId, members) = await SubmitReadySessionAsync(db);
        Assert.True((await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(Step1ApproverId), null, null)).Success);

        // A member withdraws after submit (data edited outside the locked UI) — the server re-checks the quorum.
        var member = await db.SelectionCommitteeMembers.SingleAsync(m => m.Id == members[2].Id);
        member.Recused = true;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var (ok, error) = await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(Step2ApproverId), null, null);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.QuorumMessage, error);
        Assert.Equal(2, await CurrentStepAsync(db, sessionId));
    }

    [Fact]
    public async Task Step3_approval_by_the_creator_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (sessionId, _) = await SubmitReadySessionAsync(db);
        Assert.True((await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(Step1ApproverId), null, null)).Success);
        Assert.True((await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(Step2ApproverId), null, null)).Success);

        var (ok, error) = await CreateLifecycle(db)
            .ApproveStepAsync(sessionId, Actor(SecretaryId, "hr-management-approver"), null, null);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage, error);
        Assert.Equal(3, await CurrentStepAsync(db, sessionId));
    }

    [Fact]
    public async Task Creator_is_blocked_by_the_action_service_for_any_caller()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (sessionId, _) = await SubmitReadySessionAsync(db);
        var workflowId = (await db.SelectionCommitteeSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId)).WorkflowInstanceId!.Value;

        // Bypassing the lifecycle service: the action service still refuses (CommitteeApprovalGate, PLAN-25).
        var result = await Hcm027TestSupport.CreateActionService(db)
            .ApproveAsync(workflowId, Actor(SecretaryId), null, targetStepLevel: null);

        Assert.False(result.Success);
        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage, result.Error);
    }

    [Fact]
    public async Task Step3_approval_by_a_member_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var (sessionId, members) = await SubmitReadySessionAsync(db);
        Assert.True((await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(Step1ApproverId), members[1].EmployeeId, null)).Success);
        Assert.True((await CreateLifecycle(db).ApproveStepAsync(sessionId, Actor(Step2ApproverId), members[1].EmployeeId, null)).Success);

        var (ok, error) = await CreateLifecycle(db)
            .ApproveStepAsync(sessionId, Actor(DirectorId, "hr-management-approver"), members[1].EmployeeId, null);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage, error);
        db.ChangeTracker.Clear();
        Assert.Null((await db.SelectionCommitteeSessions.SingleAsync()).ApprovedAt);
    }

    [Fact]
    public async Task Valid_path_sets_ApprovedAt()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var candidates = await SeedCandidatesAsync(db, req, 90, 80, 70);
        var (session, _) = await SeedReadySessionAsync(db, req, candidates);

        await ApproveAllStepsAsync(db, session.Id);

        var saved = await db.SelectionCommitteeSessions.AsNoTracking().SingleAsync();
        Assert.NotNull(saved.ApprovedAt);
        Assert.NotNull(saved.LockedAt);
        var (ok, error) = await CreateLifecycle(db).ApproveStepAsync(session.Id, Actor(DirectorId), null, null);
        Assert.False(ok);
        Assert.Contains("مسبقاً", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_without_submit_is_rejected()
    {
        await using var db = Hcm027TestSupport.CreateContext();
        var req = await SeedRequisitionAsync(db);
        var session = await SeedSessionAsync(db, req);

        var (ok, error) = await CreateLifecycle(db).ApproveStepAsync(session.Id, Actor(Step1ApproverId), null, null);

        Assert.False(ok);
        Assert.Equal(SelectionCommitteeLifecycleService.NoWorkflowMessage, error);
    }
}
