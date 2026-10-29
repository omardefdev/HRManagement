using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-25 (G-02 / G-14, AC-HCM-25h): the repair run lists approvals left without a workflow and orphan RECRUITMENT-HIRE
/// instances; apply starts exactly those workflows (audited) and cancels the orphans; a second run finds nothing; a
/// missing definition is reported, not thrown.
/// </summary>
public sealed class Hcm025ApprovalRepairTests
{
    private static readonly Guid Operator = Guid.Parse("25250000-0000-4000-8000-0000000000ff");

    [Fact]
    public async Task Dry_run_lists_every_stuck_row_and_changes_nothing()
    {
        await using var db = CreateContext();
        var seed = await SeedStuckAsync(db);
        var repair = CreateRepair(db);

        var report = await repair.RunAsync(dryRun: true, Operator, "operator");

        Assert.True(report.DryRun);
        Assert.Equal(6, report.Found);
        Assert.All(report.Items, i => Assert.Equal(ApprovalRepairService.Listed, i.Result));
        Assert.Equal(seed.AllIds.OrderBy(x => x), report.Items.Select(i => i.EntityId).OrderBy(x => x));
        db.ChangeTracker.Clear();
        Assert.Null((await db.RecruitmentRequisitions.SingleAsync(r => r.Id == seed.RequisitionId)).WorkflowInstanceId);
        Assert.Equal(WorkflowStatus.InProgress, (await db.WorkflowInstances.SingleAsync(i => i.Id == seed.OrphanInstanceId)).Status);
        Assert.Equal(seed.InstancesBefore, await db.WorkflowInstances.CountAsync());
        Assert.False(await db.AuditLogs.AnyAsync(a => a.Action == ApprovalRestartRules.RestartAuditAction));
    }

    [Fact]
    public async Task Apply_repairs_exactly_the_listed_rows_and_is_idempotent()
    {
        await using var db = CreateContext();
        var seed = await SeedStuckAsync(db);
        var repair = CreateRepair(db);

        var report = await repair.RunAsync(dryRun: false, Operator, "operator");
        var again = await repair.RunAsync(dryRun: false, Operator, "operator");

        Assert.Equal(6, report.Repaired);
        Assert.Equal(0, report.Failed);
        Assert.Empty(again.Items);
        db.ChangeTracker.Clear();

        Assert.Equal("RECRUITMENT-REQUISITION-APPROVAL", await GetWorkflowCodeAsync(db,
            (await db.RecruitmentRequisitions.SingleAsync(r => r.Id == seed.RequisitionId)).WorkflowInstanceId));
        Assert.Equal(RequisitionStatus.PendingApproval, (await db.RecruitmentRequisitions.SingleAsync(r => r.Id == seed.RequisitionId)).Status);
        Assert.Equal("RECRUITMENT-OFFER-APPROVAL", await GetWorkflowCodeAsync(db,
            (await db.OfferLetters.SingleAsync(o => o.Id == seed.OfferId)).WorkflowInstanceId));
        Assert.Equal("RECRUITMENT-SELECTION-COMMITTEE", await GetWorkflowCodeAsync(db,
            (await db.SelectionCommitteeSessions.SingleAsync(s => s.Id == seed.SessionId)).WorkflowInstanceId));
        Assert.Equal("RECRUITMENT-APPEAL-REVIEW", await GetWorkflowCodeAsync(db,
            (await db.RecruitmentAppeals.SingleAsync(a => a.Id == seed.AppealId)).WorkflowInstanceId));
        Assert.Equal("ESTABLISHMENT-CHANGE-APPROVAL", await GetWorkflowCodeAsync(db,
            (await db.EstablishmentChangeRequests.SingleAsync(r => r.Id == seed.ChangeRequestId)).WorkflowInstanceId));

        var orphan = await db.WorkflowInstances.SingleAsync(i => i.Id == seed.OrphanInstanceId);
        Assert.Equal(WorkflowStatus.Cancelled, orphan.Status);
        Assert.Contains("G-14", orphan.StepHistoryJson);

        // One audit row per repaired entity; the rows that were not stuck are untouched.
        Assert.Equal(5, await db.AuditLogs.CountAsync(a => a.Action == ApprovalRestartRules.RestartAuditAction && a.UserId == Operator));
        Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == ApprovalRepairService.OrphanHireCancelAuditAction));
        Assert.Null((await db.RecruitmentRequisitions.SingleAsync(r => r.Id == seed.DraftRequisitionId)).WorkflowInstanceId);
        Assert.Equal(seed.InstancesBefore + 5, await db.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task Missing_definition_is_reported_not_thrown_and_the_row_stays_listed()
    {
        await using var db = CreateContext();
        var seed = await SeedStuckAsync(db);
        // Unpublish the requisition definition only.
        foreach (var d in await db.WorkflowDefinitions.Where(d => d.Code == "RECRUITMENT-REQUISITION-APPROVAL").ToListAsync())
            d.IsPublished = false;
        await db.SaveChangesAsync();
        var repair = CreateRepair(db);

        var report = await repair.RunAsync(dryRun: false, Operator, "operator");

        var requisitionItem = Assert.Single(report.Items, i => i.EntityId == seed.RequisitionId);
        Assert.Equal(ApprovalRepairService.Failed, requisitionItem.Result);
        Assert.Equal(ApprovalStartCoordinator.MissingDefinitionMessage, requisitionItem.Error);
        Assert.Equal(5, report.Repaired);
        db.ChangeTracker.Clear();
        var req = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == seed.RequisitionId);
        Assert.Equal(RequisitionStatus.PendingApproval, req.Status);
        Assert.Null(req.WorkflowInstanceId);
        Assert.Single((await repair.RunAsync(dryRun: true, Operator, "operator")).Items);
    }

    // ---------------------------------------------------------------- fixtures

    private sealed record Seed(
        Guid RequisitionId,
        Guid DraftRequisitionId,
        Guid OfferId,
        Guid SessionId,
        Guid AppealId,
        Guid ChangeRequestId,
        Guid OrphanInstanceId,
        Guid OrphanApplicationId,
        int InstancesBefore)
    {
        public IEnumerable<Guid> AllIds => [RequisitionId, OfferId, SessionId, AppealId, ChangeRequestId, OrphanApplicationId];
    }

    private static IConfiguration NonLab() => Config("Off", labScale: false);

    private static ApprovalRepairService CreateRepair(HRCoreDbContext db)
    {
        var config = NonLab();
        var starter = CreateStarter(db);
        var policy = Mock.Of<IPolicyOrchestrator>();
        var legal = new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db));
        return new ApprovalRepairService(
            db,
            new RecruitmentRequisitionLifecycleService(db, policy, legal, starter, config,
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            CreateOfferService(db, config),
            new SelectionCommitteeLifecycleService(db, policy, starter, config, new SelectionCommitteeService(db), CreateActionService(db)),
            Hcm028AppealTestSupport.CreateService(db, config),
            new EstablishmentChangeLifecycleService(db, Mock.Of<ICodeGenerator>(), policy,
                new EstablishmentLegalSourceValidator(legal), Mock.Of<IEstablishmentChangeApplier>(), starter, config));
    }

    /// <summary>One stuck row of each kind (as a pre-PLAN-25 build left them) plus rows that are not stuck.</summary>
    private static async Task<Seed> SeedStuckAsync(HRCoreDbContext db)
    {
        await SeedReferenceDataAsync(db);

        var stuck = await SeedRequisitionAsync(db, "R25A");
        stuck.Status = RequisitionStatus.PendingApproval;
        var draft = await SeedRequisitionAsync(db, "R25B");
        draft.Status = RequisitionStatus.Draft;

        var published = await SeedRequisitionAsync(db, "R25C");
        var offerApp = await SeedApplicationAsync(db, published, RecruitmentApplicationStatus.OfferExtended, 1, "مرشح العرض");
        var offer = new OfferLetter
        {
            OfferNumber = "OFF-R25-001",
            RecruitmentApplicationId = offerApp.Id,
            FundedPositionId = published.FundedPositionId,
            Status = OfferStatus.PendingApproval,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);

        var (session, sessionError) = await new SelectionCommitteeService(db).CreateSessionAsync(
            published.Id, DateTime.UtcNow, null, null, CommitteePurpose.Ranking);
        Assert.True(session is not null, sessionError);
        session!.SubmittedAt = DateTime.UtcNow.AddDays(-2);
        session.LockedAt = session.SubmittedAt;

        // An assigned appeal whose review workflow is missing (cancelled and unlinked, as a broken earlier build left it).
        var (_, rejected) = await Hcm028AppealTestSupport.SeedRejectedAsync(db, "R25");
        var appealService = Hcm028AppealTestSupport.CreateService(db, NonLab());
        var (appeal, _) = await Hcm028AppealTestSupport.FileAndAssignAsync(db, appealService, rejected);
        var tracked = await db.RecruitmentAppeals.SingleAsync(a => a.Id == appeal.Id);
        var appealInstance = await db.WorkflowInstances.SingleAsync(i => i.Id == tracked.WorkflowInstanceId);
        appealInstance.Status = WorkflowStatus.Cancelled;
        tracked.WorkflowInstanceId = null;

        var change = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-R25-001",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"G5","OccupationalGroup":"JC-ADM"}""",
            RequestedAt = DateTime.UtcNow
        };
        db.EstablishmentChangeRequests.Add(change);

        var hireDefinition = await db.WorkflowDefinitions.SingleAsync(d => d.Code == "RECRUITMENT-HIRE");
        var orphanApp = await SeedApplicationAsync(db, published, RecruitmentApplicationStatus.Hired, 2, "معيَّن سابقاً");
        var orphan = new WorkflowInstance
        {
            DefinitionId = hireDefinition.Id,
            DefinitionVersion = hireDefinition.Version,
            EntityType = ApprovalRepairService.OrphanHireEntityType,
            EntityId = orphanApp.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddDays(-5)
        };
        db.WorkflowInstances.Add(orphan);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        return new Seed(stuck.Id, draft.Id, offer.Id, session.Id, appeal.Id, change.Id, orphan.Id, orphanApp.Id,
            await db.WorkflowInstances.CountAsync());
    }
}
