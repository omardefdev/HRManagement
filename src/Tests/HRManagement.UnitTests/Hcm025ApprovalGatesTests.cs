using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-25 (G-04, decision 5): the per-entity approval gates, through <see cref="WorkflowApprovalEvaluator"/> (the server
/// and panel decision) and, for state, through <see cref="WorkflowInstanceActionService"/>. One test per rule moved from
/// <c>ValidateSelfApprovalAsync</c> and <c>SelectionCommitteeLifecycleService.ApproveStepAsync</c>, plus the new reject
/// coverage, the assigned appeal reviewer and branch scope.
/// </summary>
public sealed class Hcm025ApprovalGatesTests
{
    private static readonly Guid Creator = Guid.Parse("25250000-0000-4000-8000-00000000c0de");
    private static readonly Guid Other = Guid.Parse("25250000-0000-4000-8000-00000000000a");
    private static readonly Guid Other2 = Guid.Parse("25250000-0000-4000-8000-00000000000b");

    private static WorkflowActionActor HrCore(Guid id) => new(id, "hr", HrManagementRoles.HrCore);
    private static WorkflowActionActor Approver(Guid id) => new(id, "approver", HrManagementRoles.HrManagementApprover);
    private static WorkflowActionActor Both(Guid id, Guid? employeeId = null) =>
        new(id, "both", Roles(HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover), employeeId);
    private static IReadOnlySet<string> Roles(params string[] roles) => roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- requisition (old rule: creator SoD)

    [Fact]
    public async Task Requisition_creator_cannot_approve_or_reject()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();

        Assert.Equal(RequisitionApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Approve));
        Assert.Equal(RequisitionApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Reject));
        Assert.Null(await fx.BlockAsync(instance, HrCore(Other), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, HrCore(Other), WorkflowActionKind.Reject));
    }

    [Fact]
    public async Task Server_reject_by_the_creator_is_refused_and_the_instance_is_unchanged()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, instance) = await fx.RequisitionAsync();

        var result = await CreateActionService(fx.Db).RejectAsync(instance.Id, HrCore(Creator), "no");

        Assert.False(result.Success);
        Assert.Equal(RequisitionApprovalGate.CreatorMessage, result.Error);
        fx.Db.ChangeTracker.Clear();
        Assert.Equal(WorkflowStatus.InProgress, (await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id)).Status);
    }

    [Fact]
    public async Task Requisition_branch_actor_acts_in_own_branch_only()
    {
        await using var fx = await Fx.CreateAsync();
        var (_, inBranch) = await fx.RequisitionAsync(fx.Branch1);
        var (_, otherBranch) = await fx.RequisitionAsync(fx.Branch2);
        var branchActor = new WorkflowActionActor(Other, "br1",
            Roles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore), ScopeOrgUnitClaim: fx.Branch1.ToString("D"));

        Assert.Null(await fx.BlockAsync(inBranch, branchActor, WorkflowActionKind.Approve));
        Assert.Equal(WorkflowGateOrgScope.OutOfBranchMessage, await fx.BlockAsync(otherBranch, branchActor, WorkflowActionKind.Approve));
        Assert.Equal(WorkflowGateOrgScope.OutOfBranchMessage, await fx.BlockAsync(otherBranch, branchActor, WorkflowActionKind.Reject));
        var invalidClaim = branchActor with { ScopeOrgUnitClaim = "not-a-guid" };
        Assert.Equal(WorkflowGateOrgScope.DeniedMessage, await fx.BlockAsync(inBranch, invalidClaim, WorkflowActionKind.Approve));
    }

    // ---------------------------------------------------------------- offer (old rules: creator SoD, override distinct approvers)

    [Fact]
    public async Task Offer_creator_cannot_approve_or_reject()
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await fx.OfferAsync(exceptionOffer: false);

        Assert.Equal(OfferApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Approve));
        Assert.Equal(OfferApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Reject));
        Assert.Null(await fx.BlockAsync(instance, HrCore(Other), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task Override_offer_needs_a_distinct_approver_for_every_step_on_approve_only()
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await fx.OfferAsync(exceptionOffer: true);
        await fx.ApprovedStepAsync(instance, Other);   // step 1 approved by Other → now at step 2

        Assert.Equal(OfferOverrideApprovalRules.SameApproverMessage,
            await fx.BlockAsync(instance, Both(Other), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, Both(Other), WorkflowActionKind.Reject));
        Assert.Null(await fx.BlockAsync(instance, Approver(Other2), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task Offer_branch_actor_outside_scope_is_refused()
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await fx.OfferAsync(exceptionOffer: false, orgUnit: fx.Branch2);
        var branchActor = new WorkflowActionActor(Other, "br1",
            Roles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore), ScopeOrgUnitClaim: fx.Branch1.ToString("D"));

        Assert.Equal(WorkflowGateOrgScope.OutOfBranchMessage, await fx.BlockAsync(instance, branchActor, WorkflowActionKind.Approve));
    }

    // ---------------------------------------------------------------- committee (old: creator; lifecycle: quorum, member at step 3)

    [Fact]
    public async Task Committee_creator_cannot_approve_or_reject()
    {
        await using var fx = await Fx.CreateAsync();
        var (instance, _) = await fx.CommitteeAsync(activeMembers: 3);

        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
            await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Approve));
        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
            await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Reject));
    }

    [Fact]
    public async Task Committee_quorum_step_needs_three_active_members_to_approve_but_may_be_rejected()
    {
        await using var fx = await Fx.CreateAsync();
        var (twoMembers, _) = await fx.CommitteeAsync(activeMembers: 2);
        await fx.ApprovedStepAsync(twoMembers, Other);   // → quorum step (2)
        var (threeMembers, _) = await fx.CommitteeAsync(activeMembers: 3);
        await fx.ApprovedStepAsync(threeMembers, Other);

        Assert.Equal(SelectionCommitteeRules.QuorumMessage, await fx.BlockAsync(twoMembers, HrCore(Other2), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(twoMembers, HrCore(Other2), WorkflowActionKind.Reject));
        Assert.Null(await fx.BlockAsync(threeMembers, HrCore(Other2), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task Committee_member_cannot_approve_or_reject_the_final_step()
    {
        await using var fx = await Fx.CreateAsync();
        var (instance, memberEmployeeId) = await fx.CommitteeAsync(activeMembers: 3);
        await fx.ApprovedStepAsync(instance, Other);
        await fx.ApprovedStepAsync(instance, Other2);   // → final step (3)
        var director = Guid.NewGuid();

        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
            await fx.BlockAsync(instance, Approver(director) with { EmployeeId = memberEmployeeId }, WorkflowActionKind.Approve));
        Assert.Equal(SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
            await fx.BlockAsync(instance, Approver(director) with { EmployeeId = memberEmployeeId }, WorkflowActionKind.Reject));
        Assert.Null(await fx.BlockAsync(instance, Approver(director) with { EmployeeId = Guid.NewGuid() }, WorkflowActionKind.Approve));
    }

    // ---------------------------------------------------------------- appeal (old: explicit outcome; new: assigned reviewer)

    [Fact]
    public async Task Appeal_only_the_assigned_reviewer_acts_and_approve_needs_the_explicit_outcome()
    {
        await using var fx = await Fx.CreateAsync();
        var reviewerEmployee = Guid.NewGuid();
        var (instance, appealId) = await fx.AppealAsync(reviewerEmployee);
        var reviewer = new WorkflowActionActor(Other, "reviewer", Roles(HrManagementRoles.Compliance), reviewerEmployee);
        var otherCompliance = new WorkflowActionActor(Other2, "c2", Roles(HrManagementRoles.Compliance), Guid.NewGuid());

        Assert.Equal(AppealApprovalGate.ExplicitOutcomeMessage, await fx.BlockAsync(instance, reviewer, WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, reviewer, WorkflowActionKind.Reject));
        Assert.Equal(RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage,
            await fx.BlockAsync(instance, otherCompliance, WorkflowActionKind.Reject));
        Assert.Equal(WorkflowStageGuard.StepRoleMessage(HrManagementRoles.Compliance),
            await fx.BlockAsync(instance, HrCore(Other) with { EmployeeId = reviewerEmployee }, WorkflowActionKind.Approve));

        var appeal = await fx.Db.RecruitmentAppeals.SingleAsync(a => a.Id == appealId);
        appeal.Outcome = RecruitmentAppealOutcome.Dismissed;   // what DecideAsync stores before approving
        Assert.Null(await fx.BlockAsync(instance, reviewer, WorkflowActionKind.Approve));
    }

    // ---------------------------------------------------------------- competition / results (old rules)

    [Fact]
    public async Task Competition_creator_cannot_approve_or_reject_and_branch_users_are_refused()
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await fx.CompetitionAsync();
        var branchActor = new WorkflowActionActor(Other, "br1",
            Roles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore), ScopeOrgUnitClaim: fx.Branch1.ToString("D"));

        Assert.Equal(CompetitionApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Approve));
        Assert.Equal(CompetitionApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Reject));
        Assert.Equal(WorkflowGateOrgScope.CentralOnlyMessage, await fx.BlockAsync(instance, branchActor, WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, HrCore(Other), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task Results_computer_submitter_and_tie_resolver_are_excluded_and_steps_need_distinct_approvers()
    {
        await using var fx = await Fx.CreateAsync();
        var computer = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var tieResolver = Guid.NewGuid();
        var instance = await fx.ResultsAsync(computer, submitter, tieResolver);

        Assert.Equal(ResultsApprovalGate.ComputedByMessage, await fx.BlockAsync(instance, HrCore(computer), WorkflowActionKind.Approve));
        Assert.Equal(ResultsApprovalGate.ComputedByMessage, await fx.BlockAsync(instance, HrCore(computer), WorkflowActionKind.Reject));
        Assert.Equal(ResultsApprovalGate.SubmitterMessage, await fx.BlockAsync(instance, HrCore(submitter), WorkflowActionKind.Approve));
        Assert.Equal(ResultsApprovalGate.TieResolverMessage, await fx.BlockAsync(instance, HrCore(tieResolver), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, HrCore(Other), WorkflowActionKind.Approve));

        await fx.ApprovedStepAsync(instance, Other);
        Assert.Equal(ResultsApprovalGate.DistinctApproverMessage, await fx.BlockAsync(instance, Both(Other), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, Both(Other), WorkflowActionKind.Reject));
        Assert.Null(await fx.BlockAsync(instance, Approver(Other2), WorkflowActionKind.Approve));
    }

    // ---------------------------------------------------------------- establishment (old rule: creator SoD; central only)

    [Fact]
    public async Task Establishment_creator_cannot_approve_or_reject_and_branch_users_are_refused()
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await fx.EstablishmentAsync();
        var branchActor = new WorkflowActionActor(Other, "br1",
            Roles(HrManagementRoles.BranchManager, HrManagementRoles.HrCore), ScopeOrgUnitClaim: fx.Branch1.ToString("D"));

        Assert.Equal(EstablishmentChangeApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Approve));
        Assert.Equal(EstablishmentChangeApprovalGate.CreatorMessage, await fx.BlockAsync(instance, HrCore(Creator), WorkflowActionKind.Reject));
        Assert.Equal(WorkflowGateOrgScope.CentralOnlyMessage, await fx.BlockAsync(instance, branchActor, WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, HrCore(Other), WorkflowActionKind.Approve));
    }

    // ---------------------------------------------------------------- step role on the server path

    [Fact]
    public async Task Server_refuses_step_two_without_the_approver_role_on_approve_and_reject_and_state_is_unchanged()
    {
        await using var fx = await Fx.CreateAsync();
        var (requisitionId, instance) = await fx.RequisitionAsync();
        var actions = CreateActionService(fx.Db);
        Assert.True((await actions.ApproveAsync(instance.Id, HrCore(Other), "1", null)).Success);

        var approve = await actions.ApproveAsync(instance.Id, HrCore(Other2), "2", null);
        var reject = await actions.RejectAsync(instance.Id, HrCore(Other2), "2");
        var admin = await actions.ApproveAsync(instance.Id, new WorkflowActionActor(Other2, "admin", HrManagementRoles.Admin), "2", null);

        var expected = WorkflowStageGuard.StepRoleMessage(HrManagementRoles.HrManagementApprover);
        Assert.Equal(expected, approve.Error);
        Assert.Equal(expected, reject.Error);
        Assert.Equal(expected, admin.Error);   // Workflow:AdminBypass is false by default
        fx.Db.ChangeTracker.Clear();
        var saved = await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
        Assert.Equal(2, saved.CurrentStepLevel);
        Assert.Equal(WorkflowStatus.InProgress, saved.Status);
        Assert.Equal(RequisitionStatus.PendingApproval, (await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId)).Status);

        Assert.True((await actions.ApproveAsync(instance.Id, Approver(Other2), "2", null)).Success);
        fx.Db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Approved, (await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId)).Status);
    }

    [Fact]
    public async Task Non_enforced_entity_types_keep_their_previous_behaviour()
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await fx.StartAsync("TRANSFER-APPROVAL", "TransferRequest", Guid.NewGuid(), Creator);

        Assert.Null(await fx.BlockAsync(instance, Approver(Other), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, Approver(Other), WorkflowActionKind.Reject));
    }

    // ---------------------------------------------------------------- fixture

    internal sealed class Fx : IAsyncDisposable
    {
        private Fx(HRCoreDbContext db, Guid hq, Guid branch1, Guid branch2)
        {
            Db = db;
            Hq = hq;
            Branch1 = branch1;
            Branch2 = branch2;
            Evaluator = WorkflowApprovalEvaluator.CreateDefault(db);
        }

        public HRCoreDbContext Db { get; }
        public Guid Hq { get; }
        public Guid Branch1 { get; }
        public Guid Branch2 { get; }
        public WorkflowApprovalEvaluator Evaluator { get; }

        public static async Task<Fx> CreateAsync()
        {
            var db = CreateContext();
            await SeedReferenceDataAsync(db);
            var hq = new OrgUnit { Code = "HQ-25", NameAr = "المركز", NameEn = "HQ" };
            hq.MaterializedPath = $"/{hq.Id:D}/";
            var b1 = new OrgUnit { Code = "B1-25", NameAr = "فرع 1", NameEn = "B1", ParentId = hq.Id };
            b1.MaterializedPath = $"{hq.MaterializedPath}{b1.Id:D}/";
            var b2 = new OrgUnit { Code = "B2-25", NameAr = "فرع 2", NameEn = "B2", ParentId = hq.Id };
            b2.MaterializedPath = $"{hq.MaterializedPath}{b2.Id:D}/";
            db.OrgUnits.AddRange(hq, b1, b2);
            await db.SaveChangesAsync();
            return new Fx(db, hq.Id, b1.Id, b2.Id);
        }

        public async Task<string?> BlockAsync(WorkflowInstance instance, WorkflowActionActor actor, WorkflowActionKind kind)
        {
            var current = await Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
            var definition = await Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == current.DefinitionId);
            return await Evaluator.GetBlockReasonAsync(current, definition, actor, kind);
        }

        public async Task<WorkflowInstance> StartAsync(string code, string entityType, Guid entityId, Guid? initiator)
        {
            var id = await new WorkflowStarter(Db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance)
                .StartByWorkflowCodeAsync(code, entityType, entityId, null, initiator, "initiator");
            return await Db.WorkflowInstances.SingleAsync(i => i.Id == id);
        }

        /// <summary>Records <paramref name="approver"/> on the current step and moves to the next one.</summary>
        public async Task ApprovedStepAsync(WorkflowInstance instance, Guid approver)
        {
            var tracked = await Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
            var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(tracked.StepHistoryJson) ?? [];
            history.Add(new WorkflowStepHistoryEntry { Level = tracked.CurrentStepLevel, Status = WorkflowStepStatus.Approved, ActorId = approver });
            tracked.StepHistoryJson = JsonSerializer.Serialize(history);
            tracked.CurrentStepLevel++;
            await Db.SaveChangesAsync();
        }

        public async Task<(Guid RequisitionId, WorkflowInstance Instance)> RequisitionAsync(Guid? orgUnit = null)
        {
            var position = await PositionAsync(orgUnit ?? Branch1);
            var req = new RecruitmentRequisition
            {
                RequisitionNumber = $"REQ-G25-{Guid.NewGuid():N}"[..16],
                FundedPositionId = position.Id,
                JobTitle = "محلل",
                Status = RequisitionStatus.PendingApproval,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1,
                LegalSourceId = Syr001LegislationIds.Law50_2004,
                CreatedBy = Creator
            };
            Db.RecruitmentRequisitions.Add(req);
            await Db.SaveChangesAsync();
            var instance = await StartAsync("RECRUITMENT-REQUISITION-APPROVAL", "RecruitmentRequisition", req.Id, Creator);
            req.WorkflowInstanceId = instance.Id;
            await Db.SaveChangesAsync();
            return (req.Id, instance);
        }

        public async Task<WorkflowInstance> OfferAsync(bool exceptionOffer, Guid? orgUnit = null)
        {
            var position = await PositionAsync(orgUnit ?? Branch1);
            var req = new RecruitmentRequisition
            {
                RequisitionNumber = $"REQ-O25-{Guid.NewGuid():N}"[..16],
                FundedPositionId = position.Id,
                JobTitle = "محلل",
                Status = RequisitionStatus.Published,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1
            };
            Db.RecruitmentRequisitions.Add(req);
            await Db.SaveChangesAsync();
            var app = await SeedApplicationAsync(Db, req, RecruitmentApplicationStatus.OfferExtended, 1);
            var offer = new OfferLetter
            {
                OfferNumber = $"OFF-25-{Guid.NewGuid():N}"[..14],
                RecruitmentApplicationId = app.Id,
                FundedPositionId = position.Id,
                Status = OfferStatus.PendingApproval,
                IsMeritOverride = exceptionOffer,
                CreatedBy = Creator
            };
            Db.OfferLetters.Add(offer);
            await Db.SaveChangesAsync();
            var code = exceptionOffer ? OfferOverrideApprovalRules.WorkflowCode : "RECRUITMENT-OFFER-APPROVAL";
            return await StartAsync(code, "OfferLetter", offer.Id, Creator);
        }

        public async Task<(WorkflowInstance Instance, Guid MemberEmployeeId)> CommitteeAsync(int activeMembers)
        {
            var (requisitionId, _) = await RequisitionAsync();
            var session = new SelectionCommitteeSession { RequisitionId = requisitionId, CommitteeDate = DateTime.UtcNow, CreatedBy = Creator };
            Db.SelectionCommitteeSessions.Add(session);
            var employees = new List<Guid>();
            for (var i = 0; i < 3; i++)
                employees.Add((await Hcm028TestSupport.SeedEmployeeAsync(Db)).Id);
            for (var i = 0; i < 3; i++)
                Db.SelectionCommitteeMembers.Add(new SelectionCommitteeMember
                {
                    SessionId = session.Id,
                    EmployeeId = employees[i],
                    Recused = i >= activeMembers
                });
            await Db.SaveChangesAsync();
            return (await StartAsync("RECRUITMENT-SELECTION-COMMITTEE", "SelectionCommitteeSession", session.Id, Creator), employees[0]);
        }

        public async Task<(WorkflowInstance Instance, Guid AppealId)> AppealAsync(Guid reviewerEmployeeId)
        {
            var (requisitionId, _) = await RequisitionAsync();
            var req = await Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
            var app = await SeedApplicationAsync(Db, req, RecruitmentApplicationStatus.Rejected, null, appeal: RecruitmentAppealStatus.Pending);
            var appeal = new RecruitmentAppeal
            {
                RecruitmentApplicationId = app.Id,
                Reason = "اعتراض على التقييم",
                Status = RecruitmentAppealStatus.Pending,
                IndependentReviewerEmployeeId = reviewerEmployeeId
            };
            Db.RecruitmentAppeals.Add(appeal);
            await Db.SaveChangesAsync();
            return (await StartAsync("RECRUITMENT-APPEAL-REVIEW", "RecruitmentAppeal", appeal.Id, Creator), appeal.Id);
        }

        public async Task<WorkflowInstance> CompetitionAsync()
        {
            var competition = new RecruitmentCompetition
            {
                Number = $"CMP-{Guid.NewGuid():N}"[..12],
                Title = "مسابقة",
                MaxLinesPerCandidate = 1,
                ObjectionWindowDays = 7,
                LegalSourceId = Syr001LegislationIds.Law50_2004,
                CreatedBy = Creator
            };
            Db.RecruitmentCompetitions.Add(competition);
            await Db.SaveChangesAsync();
            return await StartAsync("RECRUITMENT-COMPETITION-APPROVAL", CompetitionLifecycleService.EntityType, competition.Id, Creator);
        }

        public async Task<WorkflowInstance> ResultsAsync(Guid computer, Guid submitter, Guid tieResolver)
        {
            var competition = new RecruitmentCompetition
            {
                Number = $"CMR-{Guid.NewGuid():N}"[..12],
                Title = "مسابقة",
                LegalSourceId = Syr001LegislationIds.Law50_2004,
                MaxLinesPerCandidate = 1,
                ObjectionWindowDays = 7
            };
            Db.RecruitmentCompetitions.Add(competition);
            var computedAt = DateTime.UtcNow.AddHours(-1);
            var version = new CompetitionResultVersion
            {
                CompetitionId = competition.Id,
                ComputedBy = computer,
                ComputedAt = computedAt,
                Status = CompetitionResultVersionStatus.PendingApproval
            };
            Db.CompetitionResultVersions.Add(version);
            Db.AuditLogs.Add(new EntityAuditLog
            {
                EntityType = CompetitionLifecycleService.EntityType,
                EntityId = competition.Id,
                Action = CompetitionResultsService.ResolveTieAuditAction,
                UserId = tieResolver,
                CreatedAt = computedAt.AddMinutes(10)
            });
            await Db.SaveChangesAsync();
            return await StartAsync("RECRUITMENT-RESULTS-APPROVAL", CompetitionResultsService.EntityType, version.Id, submitter);
        }

        public async Task<WorkflowInstance> EstablishmentAsync()
        {
            var request = new EstablishmentChangeRequest
            {
                RequestNumber = $"EST-G25-{Guid.NewGuid():N}"[..16],
                ChangeType = EstablishmentChangeType.CreateJobCatalog,
                Status = EstablishmentChangeStatus.PendingApproval,
                PayloadJson = "{}",
                RequestedAt = DateTime.UtcNow,
                CreatedBy = Creator
            };
            Db.EstablishmentChangeRequests.Add(request);
            await Db.SaveChangesAsync();
            return await StartAsync("ESTABLISHMENT-CHANGE-APPROVAL", "EstablishmentChangeRequest", request.Id, Creator);
        }

        private async Task<FundedPosition> PositionAsync(Guid orgUnit)
        {
            var position = new FundedPosition
            {
                PositionCode = $"VAC-G25-{Guid.NewGuid():N}"[..16],
                GradeCode = "G5",
                BudgetYear = 2026,
                Status = FundedPositionStatus.Vacant,
                OrgUnitId = orgUnit
            };
            Db.FundedPositions.Add(position);
            await Db.SaveChangesAsync();
            return position;
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }
}
