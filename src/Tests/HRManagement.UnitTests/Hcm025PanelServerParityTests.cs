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
