using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.Policy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>HCM-027 (B): merit override offers use a three-step workflow with distinct approvers.</summary>
public class Hcm027OverrideWorkflowTests
{
    [Fact]
    public async Task Override_offer_starts_override_workflow_and_normal_offer_starts_standard_workflow_lab_path()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        var (overrideOffer, normalOffer) = await SeedTwoOffersOnTwoRequisitionsAsync(db);
        var service = CreateOfferService(db);

        Assert.True((await service.SubmitForApprovalAsync(overrideOffer.Id, CreatorId, "creator")).Success);
        Assert.True((await service.SubmitForApprovalAsync(normalOffer.Id, CreatorId, "creator")).Success);

        db.ChangeTracker.Clear();
        var savedOverride = await db.OfferLetters.SingleAsync(o => o.Id == overrideOffer.Id);
        var savedNormal = await db.OfferLetters.SingleAsync(o => o.Id == normalOffer.Id);
        Assert.Equal(OfferOverrideApprovalRules.WorkflowCode, await GetWorkflowCodeAsync(db, savedOverride.WorkflowInstanceId));
        Assert.Equal(RecruitmentOfferService.OfferApprovalWorkflowCode, await GetWorkflowCodeAsync(db, savedNormal.WorkflowInstanceId));
    }

    [Fact]
    public async Task Override_workflow_definition_has_three_steps_without_compliance()
    {
        await using var db = CreateContext();
        await WorkflowPackSeeder.SeedAsync(db);

        var definition = await db.WorkflowDefinitions.SingleAsync(d => d.Code == OfferOverrideApprovalRules.WorkflowCode);
        var steps = WorkflowDefinitionMapper.ToSnapshot(definition).Steps;

        Assert.Equal("OfferLetter", definition.DomainEntityType);
        Assert.Equal([1, 2, 3], steps.Select(s => s.Level));
        Assert.Equal(["hr-core", "hr-management-approver", "hr-management-approver"], steps.Select(s => s.RoleRequired));
    }

    [Fact]
    public async Task Same_actor_cannot_approve_steps_two_and_three()
    {
        await using var db = CreateContext();
        var instanceId = await SubmitOverrideOfferAsync(db);
        var actions = CreateActionService(db);

        Assert.True((await Approve(actions, instanceId, ApproverA)).Success);
        Assert.True((await Approve(actions, instanceId, ApproverB)).Success);

        var third = await Approve(actions, instanceId, ApproverB);
        Assert.False(third.Success);
        Assert.Equal(OfferOverrideApprovalRules.SameApproverMessage, third.Error);

        var stepOneApprover = await Approve(actions, instanceId, ApproverA);
        Assert.False(stepOneApprover.Success);
        Assert.Equal(OfferOverrideApprovalRules.SameApproverMessage, stepOneApprover.Error);
    }

    [Fact]
    public async Task Creator_cannot_approve_override_offer()
    {
        await using var db = CreateContext();
        var instanceId = await SubmitOverrideOfferAsync(db);

        var result = await Approve(CreateActionService(db), instanceId, CreatorId);

        Assert.False(result.Success);
        Assert.Equal("لا يمكن للمنشئ اعتماد عرضه الخاص.", result.Error);
    }

    [Fact]
    public async Task Three_distinct_approvers_issue_the_override_offer()
    {
        await using var db = CreateContext();
        var instanceId = await SubmitOverrideOfferAsync(db);
        var actions = CreateActionService(db);

        Assert.True((await Approve(actions, instanceId, ApproverA)).Success);
        Assert.True((await Approve(actions, instanceId, ApproverB)).Success);
        var last = await Approve(actions, instanceId, ApproverC);
        Assert.True(last.Success, last.Error);

        db.ChangeTracker.Clear();
        var instance = await db.WorkflowInstances.SingleAsync(i => i.Id == instanceId);
        var offer = await db.OfferLetters.SingleAsync(o => o.WorkflowInstanceId == instanceId);
        Assert.Equal(WorkflowStatus.Completed, instance.Status);
        Assert.Equal(OfferStatus.Issued, offer.Status);
    }

    [Fact]
    public void Distinct_approver_rule_applies_only_to_override_workflow()
    {
        Assert.False(OfferOverrideApprovalRules.IsOverrideWorkflow(RecruitmentOfferService.OfferApprovalWorkflowCode));
        Assert.True(OfferOverrideApprovalRules.IsOverrideWorkflow(OfferOverrideApprovalRules.WorkflowCode));
    }

    [Theory]
    [InlineData(true, "RECRUITMENT-OFFER-OVERRIDE-APPROVAL")]
    [InlineData(false, "RECRUITMENT-OFFER-APPROVAL")]
    public void Static_fallback_selects_workflow_by_override_fact(bool isMeritOverride, string expected)
    {
        var request = PolicyDomainFactsBuilders.ForOfferLetter(
            Guid.NewGuid(), Guid.NewGuid(), "Issue", Guid.NewGuid(), isMeritOverride: isMeritOverride);

        var decision = PolicyStaticFallback.Evaluate(request);

        Assert.True(decision.Allowed);
        Assert.Equal(expected, decision.GetWorkflowCode());
    }

    [Fact]
    public async Task Seeded_rules_select_override_workflow_only_for_override_offers_in_enforce_mode()
    {
        await using var db = CreateContext();
        await SeedReferenceDataAsync(db);
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db);
        await PolicyEngineDomainSeeds.SeedIncrementalRulesAsync(db);
        var (overrideOffer, normalOffer) = await SeedTwoOffersOnTwoRequisitionsAsync(db);
        var service = CreateOfferService(db, Config(policyMode: "Enforce"), CreateOrchestrator(db));

        var overrideResult = await service.SubmitForApprovalAsync(overrideOffer.Id, CreatorId, "creator");
        var normalResult = await service.SubmitForApprovalAsync(normalOffer.Id, CreatorId, "creator");
        Assert.True(overrideResult.Success, overrideResult.Error);
        Assert.True(normalResult.Success, normalResult.Error);

        db.ChangeTracker.Clear();
        var savedOverride = await db.OfferLetters.SingleAsync(o => o.Id == overrideOffer.Id);
        var savedNormal = await db.OfferLetters.SingleAsync(o => o.Id == normalOffer.Id);
        Assert.Equal(OfferOverrideApprovalRules.WorkflowCode, await GetWorkflowCodeAsync(db, savedOverride.WorkflowInstanceId));
        Assert.Equal(RecruitmentOfferService.OfferApprovalWorkflowCode, await GetWorkflowCodeAsync(db, savedNormal.WorkflowInstanceId));

        // Evaluated by the seeded rules (not the static fallback): R-OFF-OVERRIDE (9) precedes R-OFF-ISSUE (10).
        var decisions = await db.PolicyEnforcementLedgers.AsNoTracking().OrderBy(l => l.OccurredAt).Select(l => l.DecisionJson).ToListAsync();
        Assert.Equal(2, decisions.Count);
        Assert.Contains("R-OFF-OVERRIDE", decisions[0]);
        Assert.DoesNotContain("fallback", decisions[0]);
        Assert.DoesNotContain("R-OFF-OVERRIDE", decisions[1]);
        Assert.Contains("R-OFF-ISSUE", decisions[1]);
    }

    private static Task<WorkflowActionResult> Approve(WorkflowInstanceActionService actions, Guid instanceId, Guid actor) =>
        actions.ApproveAsync(instanceId, new WorkflowActionActor(actor, "approver", "hr-management-approver"), "ok", null);

    private static async Task<Guid> SubmitOverrideOfferAsync(HRCoreDbContext db)
    {
        await SeedReferenceDataAsync(db);
        var req = await SeedRequisitionAsync(db);
        await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 1, "الأول");
        var second = await SeedApplicationAsync(db, req, RecruitmentApplicationStatus.Ranked, 2, "الثاني");
        var service = CreateOfferService(db);

        var (offer, error) = await service.CreateDraftAsync(
            second.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId,
            isMeritOverride: true, meritOverrideReason: ValidOverrideReason);
        Assert.True(offer is not null, error);
        var (success, submitError) = await service.SubmitForApprovalAsync(offer!.Id, CreatorId, "creator");
        Assert.True(success, submitError);

        db.ChangeTracker.Clear();
        return (await db.OfferLetters.SingleAsync(o => o.Id == offer.Id)).WorkflowInstanceId!.Value;
    }

    /// <summary>Override offer for rank 2 on one requisition, normal offer for rank 1 on another.</summary>
    private static async Task<(OfferLetter Override, OfferLetter Normal)> SeedTwoOffersOnTwoRequisitionsAsync(HRCoreDbContext db)
    {
        var service = CreateOfferService(db);

        var reqA = await SeedRequisitionAsync(db, "A");
        await SeedApplicationAsync(db, reqA, RecruitmentApplicationStatus.Ranked, 1, "أ-1");
        var secondA = await SeedApplicationAsync(db, reqA, RecruitmentApplicationStatus.Ranked, 2, "أ-2");
        var (overrideOffer, e1) = await service.CreateDraftAsync(
            secondA.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId,
            isMeritOverride: true, meritOverrideReason: ValidOverrideReason);
        Assert.True(overrideOffer is not null, e1);

        var reqB = await SeedRequisitionAsync(db, "B");
        var firstB = await SeedApplicationAsync(db, reqB, RecruitmentApplicationStatus.Ranked, 1, "ب-1");
        var (normalOffer, e2) = await service.CreateDraftAsync(
            firstB.Id, 5000, "G5", DateTime.UtcNow.AddDays(30), null, CreatorId);
        Assert.True(normalOffer is not null, e2);

        return (overrideOffer!, normalOffer!);
    }
}
