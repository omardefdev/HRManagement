using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Policy;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Onboarding;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Movements.Infrastructure.Messaging;
using HRManagement.SharedKernel.Workflow;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests.Messaging;

/// <summary>
/// PLAN-40 (§2, class A): each site that used to publish after its last save now stages the event before a single save,
/// so the <c>OutboxMessage</c> row is written by the same <c>SaveChanges</c> as the state.
/// </summary>
public sealed class Msg040ClassAPublishOrderTests
{
    [Fact]
    public void Movement_events_carry_message_ids_derived_from_the_request()
    {
        var id = Guid.NewGuid();
        Assert.Equal(DeterministicMessageId.For<IPromotionExecuted>(id), MovementExecutedEvents.PromotionMessageId(id));
        Assert.Equal(DeterministicMessageId.For<IDelegationExecuted>(id), MovementExecutedEvents.DelegationMessageId(id));
        Assert.NotEqual(MovementExecutedEvents.PromotionMessageId(id), MovementExecutedEvents.DelegationMessageId(id));
    }

    private static async Task<(HRCoreDbContext Db, WorkflowInstanceActionService Actions, WorkflowInstance Instance)> AssetWorkflowAsync(
        OutboxOrderProbe probe, string entityType)
    {
        var db = new HRCoreDbContext(probe.InMemoryOptions<HRCoreDbContext>());
        await AssetAssignmentWorkflowSeeder.SeedAsync(db);
        var definition = await db.WorkflowDefinitions.SingleAsync();
        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = entityType,
            EntityId = Guid.NewGuid(),
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = Guid.NewGuid(),
            ContextJson = $$"""{"assetId":"{{Guid.NewGuid()}}","employeeId":"{{Guid.NewGuid()}}","lossReportId":"{{Guid.NewGuid()}}"}"""
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        probe.Clear();

        var actions = new WorkflowInstanceActionService(
            db,
            new WorkflowEngine(),
            Mock.Of<IWorkflowSignatureService>(),
            probe.Publish,
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentRequisitionWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.OfferLetterWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.SelectionCommitteeWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Recruitment.RecruitmentAppealWorkflowHandler(db),
            new HR.Core.Infrastructure.Services.Establishment.EstablishmentChangeWorkflowHandler(db, null!));
        return (db, actions, instance);
    }

    [Theory]
    [InlineData("AssetAssignment", false, "publish:IAssetAssignmentApproved")]
    [InlineData("AssetAssignment", true, "publish:IAssetAssignmentRejected")]
    [InlineData("AssetLossReport", false, "publish:IAssetLossApproved")]
    [InlineData("AssetLossReport", true, "publish:IAssetLossRejected")]
    public async Task Asset_workflow_outcome_is_staged_with_the_instance_save(string entityType, bool reject, string expected)
    {
        var probe = new OutboxOrderProbe();
        var (db, actions, instance) = await AssetWorkflowAsync(probe, entityType);
        await using var _ = db;
        var actor = new WorkflowActionActor(Guid.NewGuid(), "checker", "admin-logistics");

        var result = reject
            ? await actions.RejectAsync(instance.Id, actor, "no")
            : await actions.ApproveAsync(instance.Id, actor, "yes", 1);

        Assert.True(result.Success, result.Error);
        Assert.Equal([expected, "save"], probe.Log);
        var stored = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == instance.Id);
        Assert.Equal(reject ? WorkflowStatus.Rejected : WorkflowStatus.Completed, stored.Status);
        Assert.NotEqual(
            WorkflowAssetOutcomes.MessageIdFor(stored, approved: true),
            WorkflowAssetOutcomes.MessageIdFor(stored, approved: false));
    }

    [Fact]
    public async Task Startup_org_unit_resync_saves_its_messages()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new HRCoreDbContext(probe.InMemoryOptions<HRCoreDbContext>());
        db.OrgUnits.AddRange(
            new OrgUnit { Code = "U1", NameAr = "وحدة 1", NameEn = "U1" },
            new OrgUnit { Code = "U2", NameAr = "وحدة 2", NameEn = "U2" });
        await db.SaveChangesAsync();
        probe.Clear();

        var count = await new OrgUnitIntegrationService(db, probe.Publish).PublishAllOrgUnitsAsync();

        Assert.Equal(2, count);
        Assert.Equal(["publish:IOrgUnitChanged", "publish:IOrgUnitChanged", "save"], probe.Log);
    }

    [Fact]
    public async Task Agency_onboarded_is_published_before_the_save_that_records_the_application()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new HRCoreDbContext(probe.InMemoryOptions<HRCoreDbContext>());
        var path = new HR.Core.Infrastructure.OrgUnitPathService(db);
        var integration = new OrgUnitIntegrationService(db, probe.Publish);
        var svc = new AgencyProvisioningService(
            db, new ServiceCollection().BuildServiceProvider(), new AgencyOrgStructureProvisioner(db, path, integration),
            probe.Publish, NullLogger<AgencyProvisioningService>.Instance);
        var manifest = AgencyProvisioningTests.ValidManifest();

        await svc.ApplyManifestAsync(manifest);

        // The second save is the unit of work's own closing save (nothing left to write).
        var tail = probe.Log.SkipWhile(e => e != "publish:IAgencyOnboarded").ToList();
        Assert.Equal(["publish:IAgencyOnboarded", "save", "save"], tail);
        Assert.Equal(1, await db.SeedPackApplications.IgnoreQueryFilters().CountAsync(a => a.PackId == manifest.PackId));
    }

    [Fact]
    public async Task Policy_publication_stages_IPolicyPublished_before_its_save()
    {
        var probe = new OutboxOrderProbe();
        await using var db = new HRCoreDbContext(probe.InMemoryOptions<HRCoreDbContext>());
        var template = new PolicyTemplate { Domain = "Leave", Name = "MSG40", SubjectType = "Employee" };
        var draft = new PolicyTemplateVersion { PolicyTemplate = template, VersionNumber = 1, Status = PolicyVersionStatus.Draft };
        db.PolicyTemplates.Add(template);
        db.PolicyTemplateVersions.Add(draft);
        await db.SaveChangesAsync();
        probe.Clear();

        var publisher = new PolicyVersionPublisher(db, Mock.Of<IPolicyPublishedSnapshotCache>(), probe.Publish);
        await publisher.PublishDraftAsync(draft, Guid.NewGuid(), Guid.NewGuid(), "msg40");

        Assert.Equal(["publish:IPolicyPublished", "save"], probe.Log);
        Assert.Equal(PolicyVersionStatus.Published, (await db.PolicyTemplateVersions.AsNoTracking().SingleAsync()).Status);
    }
}
