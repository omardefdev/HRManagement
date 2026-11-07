using HR.Core.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using static HRManagement.UnitTests.Wf023WorkInboxUiTests;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-44 (G-WF-12): the inbox offers only the instance the entity is linked to. An approval given to any other one would be lost, because
/// the handler ignores it. The index keeps two running instances apart, so the case left is a running instance the entity does not link.
/// </summary>
public sealed class Wf044InboxLinkageTests
{
    private static async Task AssertTheLinkDecidesAsync(
        HRCoreDbContext db, Guid instanceId, Guid userId, string role, Func<Guid?, Task> linkTo)
    {
        var query = CreateQueryService(db);
        var user = CreateUser(userId, role);

        await linkTo(instanceId);
        Assert.Contains(await query.GetActionableWorkflowItemsAsync(user), i => i.Id == instanceId);

        await linkTo(Guid.NewGuid());
        Assert.DoesNotContain(await query.GetActionableWorkflowItemsAsync(user), i => i.Id == instanceId);

        await linkTo(null);
        Assert.DoesNotContain(await query.GetActionableWorkflowItemsAsync(user), i => i.Id == instanceId);
    }

    [Fact]
    public async Task A_requisition_offers_only_its_linked_instance()
    {
        await using var db = CreateContext();
        var (id, instance) = await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1);
        var entity = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == id);

        await AssertTheLinkDecidesAsync(db, instance, HrReviewerId, "hr-core", async link =>
        {
            entity.WorkflowInstanceId = link;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task An_offer_offers_only_its_linked_instance()
    {
        await using var db = CreateContext();
        var (id, instance) = await SeedTwoStepOfferAsync(db, CreatorId, stepLevel: 1);
        var entity = await db.OfferLetters.SingleAsync(o => o.Id == id);

        await AssertTheLinkDecidesAsync(db, instance, HrReviewerId, "hr-core", async link =>
        {
            entity.WorkflowInstanceId = link;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task A_committee_offers_only_its_linked_instance()
    {
        await using var db = CreateContext();
        var (session, instance) = await SeedCommitteeAsync(db, memberCount: 3);

        await AssertTheLinkDecidesAsync(db, instance.Id, HrReviewerId, "hr-core", async link =>
        {
            session.WorkflowInstanceId = link;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task An_appeal_offers_only_its_linked_instance()
    {
        await using var db = CreateContext();
        var (appeal, instance) = await SeedAppealAsync(db, ComplianceId);

        await AssertTheLinkDecidesAsync(db, instance.Id, ComplianceId, "compliance", async link =>
        {
            appeal.WorkflowInstanceId = link;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task An_establishment_change_offers_only_its_linked_instance()
    {
        await using var db = CreateContext();
        var id = await SeedEstablishmentChangeAsync(db, CreatorId, stepLevel: 1);
        var entity = await db.EstablishmentChangeRequests.SingleAsync(r => r.Id == id);
        var instance = entity.WorkflowInstanceId!.Value;

        await AssertTheLinkDecidesAsync(db, instance, HrReviewerId, "hr-core", async link =>
        {
            entity.WorkflowInstanceId = link;
            await db.SaveChangesAsync();
        });
    }
}
