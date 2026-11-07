using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Hcm025ApprovalGatesTests;

namespace HRManagement.UnitTests;

/// <summary>PLAN-44 (G-WF-19, D-WF-8): the creator, the submitter and the requester are barred from every step of the five gates.</summary>
public sealed class Wf044MakerRulesTests
{
    private static readonly Guid Creator = Guid.Parse("44000000-0000-4000-8000-0000000000d1");
    private static readonly Guid Submitter = Guid.Parse("44000000-0000-4000-8000-0000000000d2");
    private static readonly Guid Requester = Guid.Parse("44000000-0000-4000-8000-0000000000d3");

    public enum EntityKind
    {
        Requisition,
        Offer,
        Establishment,
        Competition,
        Results
    }

    public enum Maker
    {
        Creator,
        Submitter,
        Requester
    }

    public static TheoryData<EntityKind, Maker, int> Matrix()
    {
        var data = new TheoryData<EntityKind, Maker, int>();
        foreach (var kind in Enum.GetValues<EntityKind>())
            foreach (var maker in Enum.GetValues<Maker>())
                foreach (var step in new[] { 1, 2 })
                    data.Add(kind, maker, step);
        return data;
    }

    private static WorkflowActionActor Holding(Guid id) => new(
        id, "actor", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HrManagementRoles.HrCore, HrManagementRoles.HrManagementApprover });

    /// <summary>The instance's submitter and requester, and the entity's creator, each a different person.</summary>
    internal static async Task<WorkflowInstance> ArrangeAsync(Fx fx, EntityKind kind, Guid? creator, Guid? submitter, Guid? requester)
    {
        var instance = kind switch
        {
            EntityKind.Requisition => (await fx.RequisitionAsync()).Instance,
            EntityKind.Offer => await fx.OfferAsync(exceptionOffer: false),
            EntityKind.Establishment => await fx.EstablishmentAsync(),
            EntityKind.Competition => await fx.CompetitionAsync(),
            EntityKind.Results => await fx.ResultsAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var tracked = await fx.Db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
        tracked.InitiatedBy = submitter;
        tracked.RequestedByUserId = requester;
        switch (kind)
        {
            case EntityKind.Requisition:
                (await fx.Db.RecruitmentRequisitions.SingleAsync(r => r.Id == instance.EntityId)).CreatedBy = creator;
                break;
            case EntityKind.Offer:
                var offer = await fx.Db.OfferLetters.SingleAsync(o => o.Id == instance.EntityId);
                offer.CreatedBy = creator;
                offer.WorkflowInstanceId = instance.Id;
                break;
            case EntityKind.Establishment:
                var change = await fx.Db.EstablishmentChangeRequests.SingleAsync(r => r.Id == instance.EntityId);
                change.CreatedBy = creator;
                change.WorkflowInstanceId = instance.Id;
                break;
            case EntityKind.Competition:
                var competition = await fx.Db.RecruitmentCompetitions.SingleAsync(c => c.Id == instance.EntityId);
                competition.CreatedBy = creator;
                competition.ApprovalWorkflowInstanceId = instance.Id;
                break;
            case EntityKind.Results:
                var version = await fx.Db.CompetitionResultVersions.SingleAsync(v => v.Id == instance.EntityId);
                version.CreatedBy = creator;
                version.WorkflowInstanceId = instance.Id;
                break;
        }

        await fx.Db.SaveChangesAsync();
        return tracked;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task Every_maker_is_barred_from_every_step_on_approve_and_reject(EntityKind kind, Maker maker, int step)
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await ArrangeAsync(fx, kind, Creator, Submitter, Requester);
        if (step == 2)
            await fx.ApprovedStepAsync(instance, Guid.NewGuid());
        var actor = Holding(maker switch { Maker.Creator => Creator, Maker.Submitter => Submitter, _ => Requester });

        Assert.NotNull(await fx.BlockAsync(instance, actor, WorkflowActionKind.Approve));
        Assert.NotNull(await fx.BlockAsync(instance, actor, WorkflowActionKind.Reject));
    }

    [Theory]
    [InlineData(EntityKind.Requisition, RequisitionApprovalGate.CreatorMessage)]
    [InlineData(EntityKind.Offer, OfferApprovalGate.CreatorMessage)]
    [InlineData(EntityKind.Establishment, EstablishmentChangeApprovalGate.CreatorMessage)]
    [InlineData(EntityKind.Competition, CompetitionApprovalGate.CreatorMessage)]
    [InlineData(EntityKind.Results, ResultsApprovalGate.SubmitterMessage)]
    public async Task A_submitter_who_is_not_the_creator_is_refused_on_step_1_with_the_gates_message(EntityKind kind, string expected)
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await ArrangeAsync(fx, kind, Creator, Submitter, null);

        Assert.Equal(expected, await fx.BlockAsync(instance, Holding(Submitter), WorkflowActionKind.Approve));
    }

    [Theory]
    [InlineData(EntityKind.Requisition)]
    [InlineData(EntityKind.Offer)]
    [InlineData(EntityKind.Establishment)]
    [InlineData(EntityKind.Competition)]
    public async Task An_empty_created_by_leaves_the_submitter_barred(EntityKind kind)
    {
        await using var fx = await Fx.CreateAsync();
        var instance = await ArrangeAsync(fx, kind, null, Submitter, null);

        Assert.NotNull(await fx.BlockAsync(instance, Holding(Submitter), WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(instance, Holding(Guid.NewGuid()), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task The_setting_lets_a_submitter_act_on_step_1_but_never_on_results()
    {
        await using var fx = await Fx.CreateAsync();
        var options = new WorkflowStepRoleOptions { Maker = new WorkflowMakerSection { IncludeSubmitter = false } };
        var evaluator = WorkflowApprovalEvaluator.CreateDefault(fx.Db, options);

        async Task<string?> BlockAsync(WorkflowInstance instance)
        {
            var current = await fx.Db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
            var definition = await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Id == current.DefinitionId);
            return await evaluator.GetBlockReasonAsync(current, definition, Holding(Submitter), WorkflowActionKind.Approve);
        }

        Assert.Null(await BlockAsync(await ArrangeAsync(fx, EntityKind.Requisition, Creator, Submitter, null)));
        Assert.Equal(ResultsApprovalGate.SubmitterMessage, await BlockAsync(await ArrangeAsync(fx, EntityKind.Results, Creator, Submitter, null)));
    }

    [Fact]
    public void The_set_holds_who_is_there_and_nobody_for_an_empty_field()
    {
        Assert.Equal([Creator, Submitter, Requester], WorkflowMakerRules.Makers(Creator, Submitter, Requester).Order().ToArray());
        Assert.Empty(WorkflowMakerRules.Makers(null, null, null));
        Assert.DoesNotContain(Submitter, WorkflowMakerRules.Makers(Creator, Submitter, null, includeSubmitter: false));
        Assert.Contains(Requester, WorkflowMakerRules.Makers(null, Submitter, Requester, includeSubmitter: false));
    }
}
