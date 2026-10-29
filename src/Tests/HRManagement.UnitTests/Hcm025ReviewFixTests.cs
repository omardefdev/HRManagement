using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-25 P8: one regression test per independent-review finding that survived refutation (reports/plan25-final-report.md §10).
/// </summary>
public sealed class Hcm025ReviewFixTests
{
    private static readonly Guid Creator = Guid.Parse("25250000-0000-4000-8000-00000000c0de");
    private static readonly Guid Other = Guid.Parse("25250000-0000-4000-8000-00000000000a");

    // Finding 1 — the coordinator never adopts a half-approved instance or one of another workflow code.
    [Fact]
    public async Task F1_coordinator_does_not_adopt_an_acted_on_instance_or_another_workflow()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var (requisitionId, preCreated) = await fx.RequisitionAsync();
        await fx.ApprovedStepAsync(preCreated, Other);   // someone already approved step 1
        var offerInstance = await fx.OfferAsync(exceptionOffer: false);
        var offer = await fx.Db.OfferLetters.SingleAsync(o => o.Id == offerInstance.EntityId);
        var coordinator = new ApprovalStartCoordinator(fx.Db, Mock.Of<IPolicyOrchestrator>(), CreateStarter(fx.Db), Config("Off", false));

        var requisitionStart = await coordinator.StartAsync(new ApprovalStartRequest("RecruitmentRequisition", requisitionId, null,
            "RECRUITMENT-REQUISITION-APPROVAL", PolicyDomains.CoreRecruitment, Creator, "creator", "{}"));
        var overrideStart = await coordinator.StartAsync(new ApprovalStartRequest("OfferLetter", offer.Id, null,
            OfferOverrideApprovalRules.WorkflowCode, PolicyDomains.CoreRecruitment, Creator, "creator", "{}"));

        Assert.NotEqual(preCreated.Id, requisitionStart.InstanceId);
        Assert.NotEqual(offerInstance.Id, overrideStart.InstanceId);
        Assert.Equal(OfferOverrideApprovalRules.WorkflowCode, await GetWorkflowCodeAsync(fx.Db, overrideStart.InstanceId));
    }

    [Fact]
    public async Task F1_generic_start_api_refuses_entity_types_started_by_their_submit()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var controller = new WorkflowInstancesController(db, new WorkflowEngine(), CreateActionService(db), new WorkflowStepRoleOptions())
        {
            ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var result = await controller.Start(new StartWorkflowRequest { EntityType = "RecruitmentRequisition", EntityId = Guid.NewGuid() });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.WorkflowInstances.ToListAsync());
    }

    // Finding 2 — an instance that is not the entity's linked one never decides it.
    [Fact]
    public async Task F2_a_stray_instance_completing_does_not_approve_the_requisition_or_an_unsubmitted_session()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var (requisitionId, linked) = await fx.RequisitionAsync();
        var stray = await fx.StartAsync("RECRUITMENT-REQUISITION-APPROVAL", "RecruitmentRequisition", requisitionId, Other);
        var (sessionInstance, _) = await fx.CommitteeAsync(activeMembers: 3);   // never submitted

        await new RecruitmentRequisitionWorkflowHandler(fx.Db).HandleAsync(stray, approved: true, reason: null);
        await new SelectionCommitteeWorkflowHandler(fx.Db).HandleAsync(sessionInstance, approved: true, reason: null);

        fx.Db.ChangeTracker.Clear();
        var requisition = await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
        Assert.Equal(RequisitionStatus.PendingApproval, requisition.Status);
        Assert.Equal(linked.Id, requisition.WorkflowInstanceId);
        Assert.Null((await fx.Db.SelectionCommitteeSessions.SingleAsync(s => s.Id == sessionInstance.EntityId)).ApprovedAt);

        await new RecruitmentRequisitionWorkflowHandler(fx.Db).HandleAsync(
            await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == linked.Id), approved: true, reason: null);
        fx.Db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Approved, (await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId)).Status);
    }

    // Finding 3 — an admin without «compliance» is not offered appeal decisions (the server refuses them).
    [Fact]
    public void F3_appeal_reviewer_role_is_compliance_only()
    {
        static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
            roles.Select(r => new Claim(ClaimTypes.Role, r)), "test", ClaimTypes.Name, ClaimTypes.Role));

        Assert.False(RecruitmentAppealAccess.HasReviewerRole(User(HrManagementRoles.Admin)));
        Assert.True(RecruitmentAppealAccess.HasReviewerRole(User(HrManagementRoles.Compliance)));
        Assert.True(RecruitmentAppealAccess.HasReviewerRole(User(HrManagementRoles.Admin, HrManagementRoles.Compliance)));
    }

    // Finding 6 — the inbox applies the override distinct-approver rule like OfferApprovalGate.
    [Fact]
    public async Task F6_inbox_hides_the_override_step_from_the_previous_step_approver()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await fx.OfferAsync(exceptionOffer: true);
        await fx.ApprovedStepAsync(instance, Other);
        var current = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        var definition = await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == current.DefinitionId);
        var approver = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, HrManagementRoles.HrManagementApprover)], "test", ClaimTypes.Name, ClaimTypes.Role));
        var context = new WorkflowInboxEntityContext { EntityCreatedBy = Creator };
        var eligibility = new WorkflowInboxEligibilityService();

        Assert.False(eligibility.IsActionable(current, definition, approver, Other, context, OrgAccessScope.Unrestricted()));
        Assert.True(eligibility.IsActionable(current, definition, approver, Guid.NewGuid(), context, OrgAccessScope.Unrestricted()));
    }

    // Finding 9 — cancelling a pending establishment change cancels its workflow.
    [Fact]
    public async Task F9_cancelling_a_pending_establishment_change_cancels_its_workflow()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await fx.EstablishmentAsync();
        var request = await fx.Db.EstablishmentChangeRequests.SingleAsync(r => r.Id == instance.EntityId);
        request.WorkflowInstanceId = instance.Id;
        await fx.Db.SaveChangesAsync();
        var lifecycle = new EstablishmentChangeLifecycleService(fx.Db, Mock.Of<ICodeGenerator>(), Mock.Of<IPolicyOrchestrator>(),
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(fx.Db))),
            Mock.Of<IEstablishmentChangeApplier>(), CreateStarter(fx.Db), Config("Off", false));

        var (success, error) = await lifecycle.CancelAsync(request.Id);

        Assert.True(success, error);
        fx.Db.ChangeTracker.Clear();
        Assert.Equal(WorkflowStatus.Cancelled, (await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id)).Status);
    }

    // Finding 10 — a restarted workflow keeps the record's creator as initiator (the initiator rule protects the right person).
    [Fact]
    public async Task F10_restart_keeps_the_creator_as_initiator()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var (requisitionId, linked) = await fx.RequisitionAsync();
        var tracked = await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == linked.Id);
        tracked.Status = WorkflowStatus.Cancelled;
        var requisition = await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
        requisition.WorkflowInstanceId = null;
        await fx.Db.SaveChangesAsync();
        var operatorId = Guid.NewGuid();
        var lifecycle = new RecruitmentRequisitionLifecycleService(fx.Db, Mock.Of<IPolicyOrchestrator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(fx.Db)), CreateStarter(fx.Db), Config("Off", false),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

        var (success, error) = await lifecycle.RestartApprovalAsync(requisitionId, operatorId, "operator");

        Assert.True(success, error);
        fx.Db.ChangeTracker.Clear();
        var restarted = await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == requisitionId);
        Assert.Equal(Creator, (await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == restarted.WorkflowInstanceId)).InitiatedBy);
        Assert.True(await fx.Db.AuditLogs.AnyAsync(a => a.EntityId == requisitionId && a.UserId == operatorId
            && a.Action == ApprovalRestartRules.RestartAuditAction));
    }
}
