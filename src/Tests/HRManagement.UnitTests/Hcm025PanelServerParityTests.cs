using System.Security.Claims;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-25 (G-04, decision 5): for a matrix of users × workflow states, the detail panel offers approve (reject) exactly
/// when the server accepts the approve (reject). Each case runs on a fresh database, because the server call changes it.
/// </summary>
public sealed class Hcm025PanelServerParityTests
{
    private static readonly Guid Creator = Guid.Parse("25250000-0000-4000-8000-00000000c0de");
    private static readonly Guid StepOne = Guid.Parse("25250000-0000-4000-8000-0000000000a1");
    private static readonly Guid Someone = Guid.Parse("25250000-0000-4000-8000-0000000000a9");

    public enum Scenario
    {
        RequisitionStep1,
        RequisitionStep2,
        OfferStep1,
        OverrideOfferStep2ApprovedBySomeone,
        CommitteeQuorumWithTwoMembers,
        RequisitionInOtherBranch
    }

    public enum User
    {
        HrCore,
        Approver,
        Admin,
        CreatorHrCore,
        HrCoreAndApprover,
        BranchManagerBranch1,
        SomeoneWithBothRoles
    }

    public static TheoryData<Scenario, User, bool> Matrix()
    {
        var data = new TheoryData<Scenario, User, bool>();
        foreach (var scenario in Enum.GetValues<Scenario>())
            foreach (var user in Enum.GetValues<User>())
                foreach (var reject in new[] { false, true })
                    data.Add(scenario, user, reject);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Panel_offers_an_action_exactly_when_the_server_accepts_it(Scenario scenario, User user, bool reject)
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await ArrangeAsync(fx, scenario);
        var principal = Principal(user, fx);

        var current = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        var definition = await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == current.DefinitionId);
        var panel = await WorkflowPanelBuilder.BuildAsync(
            current, definition, principal, new WorkflowPanelBuildOptions { IsAwaitingApproval = true }, fx.Evaluator);

        var actions = new WorkflowInstanceActionService(
            fx.Db,
            new HRManagement.ServiceDefaults.Workflow.WorkflowEngine(),
            Moq.Mock.Of<IWorkflowSignatureService>(),
            Moq.Mock.Of<MassTransit.IPublishEndpoint>(),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionWorkflowHandler(fx.Db),
            new HR.Core.Infrastructure.Services.Recruitment.OfferLetterWorkflowHandler(fx.Db),
            new HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkflowHandler(fx.Db),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler(fx.Db),
            new HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler(fx.Db, null!),
            evaluator: fx.Evaluator);
        var actor = WorkflowActionActor.FromPrincipal(principal);
        var server = reject
            ? await actions.RejectAsync(instance.Id, actor, "parity")
            : await actions.ApproveAsync(instance.Id, actor, "parity", current.CurrentStepLevel);

        Assert.True((reject ? panel.CanReject : panel.CanApprove) == server.Success,
            $"{scenario} / {user} / {(reject ? "reject" : "approve")}: panel={(reject ? panel.CanReject : panel.CanApprove)} " +
            $"(«{panel.BlockReason}»), server={server.Success} («{server.Error}»)");
    }

    // ---------------------------------------------------------------- PLAN-44 (G-WF-19): makers, for every type

    private static readonly Guid MakerCreator = Guid.Parse("44000000-0000-4000-8000-0000000000f1");
    private static readonly Guid MakerSubmitter = Guid.Parse("44000000-0000-4000-8000-0000000000f2");
    private static readonly Guid Bystander = Guid.Parse("44000000-0000-4000-8000-0000000000f3");

    public static TheoryData<Wf044MakerRulesTests.EntityKind, bool, string> MakerMatrix()
    {
        var data = new TheoryData<Wf044MakerRulesTests.EntityKind, bool, string>();
        foreach (var kind in Enum.GetValues<Wf044MakerRulesTests.EntityKind>())
            foreach (var emptyCreatedBy in new[] { false, true })
                foreach (var who in new[] { nameof(MakerCreator), nameof(MakerSubmitter), nameof(Bystander) })
                    data.Add(kind, emptyCreatedBy, who);
        return data;
    }

    private static ClaimsPrincipal Holding(Guid id)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, HrManagementRoles.HrCore),
            new(ClaimTypes.NameIdentifier, id.ToString("D"))
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static WorkflowInstanceActionService Actions(Hcm025ApprovalGatesTests.Fx fx) => new(
        fx.Db,
        new HRManagement.ServiceDefaults.Workflow.WorkflowEngine(),
        Moq.Mock.Of<IWorkflowSignatureService>(),
        Moq.Mock.Of<MassTransit.IPublishEndpoint>(),
        new HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionWorkflowHandler(fx.Db),
        new HR.Core.Infrastructure.Services.Recruitment.OfferLetterWorkflowHandler(fx.Db),
        new HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkflowHandler(fx.Db),
        new HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler(fx.Db),
        new HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler(fx.Db, null!),
        evaluator: fx.Evaluator);

    [Theory]
    [MemberData(nameof(MakerMatrix))]
    public async Task Panel_and_server_judge_a_maker_alike_with_an_empty_created_by_and_a_submitter_who_is_not_the_creator(
        Wf044MakerRulesTests.EntityKind kind, bool emptyCreatedBy, string who)
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await Wf044MakerRulesTests.ArrangeAsync(fx, kind, emptyCreatedBy ? null : MakerCreator, MakerSubmitter, null);
        var id = who switch { nameof(MakerCreator) => MakerCreator, nameof(MakerSubmitter) => MakerSubmitter, _ => Bystander };
        var principal = Holding(id);

        var current = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        var definition = await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == current.DefinitionId);
        var panel = await WorkflowPanelBuilder.BuildAsync(
            current, definition, principal, new WorkflowPanelBuildOptions { IsAwaitingApproval = true }, fx.Evaluator);
        var server = await Actions(fx).ApproveAsync(instance.Id, WorkflowActionActor.FromPrincipal(principal), "parity", 1);

        Assert.True(panel.CanApprove == server.Success,
            $"{kind} / empty={emptyCreatedBy} / {who}: panel={panel.CanApprove} («{panel.BlockReason}»), server={server.Success} («{server.Error}»)");
        var isMaker = id == MakerSubmitter || (id == MakerCreator && !emptyCreatedBy);
        Assert.Equal(!isMaker, server.Success);
    }

    [Theory]
    [InlineData(Wf044MakerRulesTests.EntityKind.Requisition)]
    [InlineData(Wf044MakerRulesTests.EntityKind.Offer)]
    [InlineData(Wf044MakerRulesTests.EntityKind.Establishment)]
    public async Task Inbox_and_server_judge_a_maker_alike_with_an_empty_created_by_and_a_submitter_who_is_not_the_creator(
        Wf044MakerRulesTests.EntityKind kind)
    {
        foreach (var emptyCreatedBy in new[] { false, true })
        {
            foreach (var id in new[] { MakerCreator, MakerSubmitter, Bystander })
            {
                await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
                var instance = await Wf044MakerRulesTests.ArrangeAsync(fx, kind, emptyCreatedBy ? null : MakerCreator, MakerSubmitter, null);
                var principal = Holding(id);

                var items = await Wf023WorkInboxUiTests.CreateQueryService(fx.Db).GetActionableWorkflowItemsAsync(principal);
                var server = await Actions(fx).ApproveAsync(instance.Id, WorkflowActionActor.FromPrincipal(principal), "parity", 1);

                Assert.True(items.Any(i => i.Id == instance.Id) == server.Success,
                    $"{kind} / empty={emptyCreatedBy} / {id}: inbox={items.Count}, server={server.Success} («{server.Error}»)");
            }
        }
    }

    private static async Task<WorkflowInstance> ArrangeAsync(Hcm025ApprovalGatesTests.Fx fx, Scenario scenario)
    {
        switch (scenario)
        {
            case Scenario.RequisitionStep1:
                return (await fx.RequisitionAsync()).Instance;
            case Scenario.RequisitionStep2:
                {
                    var (_, instance) = await fx.RequisitionAsync();
                    await fx.ApprovedStepAsync(instance, StepOne);
                    return instance;
                }
            case Scenario.OfferStep1:
                return await fx.OfferAsync(exceptionOffer: false);
            case Scenario.OverrideOfferStep2ApprovedBySomeone:
                {
                    var instance = await fx.OfferAsync(exceptionOffer: true);
                    await fx.ApprovedStepAsync(instance, Someone);
                    return instance;
                }
            case Scenario.CommitteeQuorumWithTwoMembers:
                {
                    var (instance, _) = await fx.CommitteeAsync(activeMembers: 2);
                    await fx.ApprovedStepAsync(instance, StepOne);
                    return instance;
                }
            case Scenario.RequisitionInOtherBranch:
                return (await fx.RequisitionAsync(fx.Branch2)).Instance;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static ClaimsPrincipal Principal(User user, Hcm025ApprovalGatesTests.Fx fx)
    {
        var (id, roles, scope) = user switch
        {
            User.HrCore => (Guid.NewGuid(), new[] { HrManagementRoles.HrCore }, (Guid?)null),
            User.Approver => (Guid.NewGuid(), new[] { HrManagementRoles.HrManagementApprover }, null),
            User.Admin => (Guid.NewGuid(), new[] { HrManagementRoles.Admin }, null),
            User.CreatorHrCore => (Creator, new[] { HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover }, null),
            User.HrCoreAndApprover => (Guid.NewGuid(), new[] { HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover }, null),
            User.BranchManagerBranch1 => (Guid.NewGuid(), new[] { HrManagementRoles.BranchManager, HrManagementRoles.HrCore }, fx.Branch1),
            User.SomeoneWithBothRoles => (Someone, new[] { HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover }, null),
            _ => throw new ArgumentOutOfRangeException(nameof(user))
        };
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, id.ToString("D")));
        if (scope is Guid unit)
            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, unit.ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }
}
