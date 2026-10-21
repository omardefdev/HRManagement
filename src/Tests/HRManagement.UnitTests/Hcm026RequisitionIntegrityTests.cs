using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Hcm026RequisitionIntegrityTests
{
    private const string Reason = "إلغاء الشاغر بقرار إداري";

    [Fact]
    public void Blocking_statuses_include_published()
    {
        Assert.Equal(
            [RequisitionStatus.Draft, RequisitionStatus.PendingApproval, RequisitionStatus.Approved, RequisitionStatus.Published],
            RequisitionWorkflowRules.BlockingStatuses);
        Assert.True(RequisitionWorkflowRules.CanCancel(RequisitionStatus.Published));
        Assert.False(RequisitionWorkflowRules.CanCancel(RequisitionStatus.Closed));
    }

    [Fact]
    public async Task Second_requisition_on_position_with_published_requisition_rejected()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var published = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        var position = await db.FundedPositions.SingleAsync(p => p.Id == published.FundedPositionId);

        // Service-level check (what RecruitmentRequisitionsController.Create uses).
        var hasBlocking = await db.RecruitmentRequisitions.AnyAsync(
            r => r.FundedPositionId == position.Id && RequisitionWorkflowRules.BlockingStatuses.Contains(r.Status));
        var error = new RecruitmentRequisitionService(new EstablishmentService()).ValidateCreate(position, hasBlocking);
        Assert.NotNull(error);

        // DB guard: the filtered unique index rejects a second blocking row.
        db.RecruitmentRequisitions.Add(new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-DUP",
            FundedPositionId = position.Id,
            JobTitle = "مكرر",
            Status = RequisitionStatus.Draft,
            VacancyCount = 1
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Cancel_published_cascades_everything_in_one_save()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var fx = await SeedPublishedPipelineAsync(db);
        var actor = Guid.NewGuid();

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false)
            .CancelAsync(fx.Requisition.Id, Reason, actor, actorCanApprove: true);

        Assert.True(success, error);
        db.ChangeTracker.Clear();

        var req = await db.RecruitmentRequisitions.SingleAsync(r => r.Id == fx.Requisition.Id);
        Assert.Equal(RequisitionStatus.Cancelled, req.Status);
        Assert.Equal(Reason, req.CancellationReason);
        Assert.Equal(actor, req.CancelledBy);
        Assert.NotNull(req.CancelledAt);
        Assert.NotNull(req.ClosedDate);

        Assert.False((await db.PublicJobPostings.SingleAsync()).IsPublished);
        Assert.False((await db.InternalVacancies.SingleAsync()).IsOpen);
        Assert.Equal(OfferStatus.Revoked, (await db.OfferLetters.SingleAsync()).Status);
        Assert.Equal(WorkflowStatus.Cancelled, (await db.WorkflowInstances.SingleAsync(w => w.Id == fx.OfferWorkflowId)).Status);

        var apps = await db.RecruitmentApplications.ToDictionaryAsync(a => a.Id);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[fx.SubmittedApp].Status);
        Assert.Contains("إلغاء الشاغر: " + Reason, apps[fx.SubmittedApp].Notes);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[fx.InterviewApp].Status);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, apps[fx.OfferApp].Status);
        Assert.Equal(RecruitmentApplicationStatus.Withdrawn, apps[fx.WithdrawnApp].Status);

        var slot = await db.RecruitmentInterviewSlots.SingleAsync();
        Assert.Equal(InterviewSlotStatus.Cancelled, slot.Status);
        Assert.Equal(Reason, slot.CancelledReason);

        Assert.Single(await db.AuditLogs.Where(l => l.EntityId == req.Id && l.Action == "CancelRequisition").ToListAsync());
    }

    [Fact]
    public async Task Cancel_published_without_approve_flag_rejected()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        var lifecycle = Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false);

        var (noApprove, error) = await lifecycle.CancelAsync(req.Id, Reason, Guid.NewGuid(), actorCanApprove: false);
        Assert.False(noApprove);
        Assert.NotNull(error);

        var (shortReason, _) = await lifecycle.CancelAsync(req.Id, "قصير", Guid.NewGuid(), actorCanApprove: true);
        Assert.False(shortReason);

        db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Published, (await db.RecruitmentRequisitions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_draft_needs_no_reason_and_cancels_running_workflow()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.PendingApproval);
        var workflow = new WorkflowInstance { EntityType = nameof(RecruitmentRequisition), EntityId = req.Id, Status = WorkflowStatus.InProgress };
        db.WorkflowInstances.Add(workflow);
        req.WorkflowInstanceId = workflow.Id;
        await db.SaveChangesAsync();

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false)
            .CancelAsync(req.Id, null, Guid.NewGuid(), actorCanApprove: false);

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Cancelled, (await db.RecruitmentRequisitions.SingleAsync()).Status);
        var saved = await db.WorkflowInstances.SingleAsync();
        Assert.Equal(WorkflowStatus.Cancelled, saved.Status);
        Assert.NotNull(saved.CompletedAt);
    }

    [Theory]
    [InlineData(RecruitmentApplicationStatus.Hired)]
    [InlineData(RecruitmentApplicationStatus.OfferAccepted)]
    public async Task Cancel_blocked_with_hired_or_accepted_candidate(RecruitmentApplicationStatus status)
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        db.RecruitmentApplications.Add(NewApp(req.Id, status));
        await db.SaveChangesAsync();

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false)
            .CancelAsync(req.Id, Reason, Guid.NewGuid(), actorCanApprove: true);

        Assert.False(success);
        Assert.NotNull(error);
        db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Published, (await db.RecruitmentRequisitions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Close_blocked_with_issued_offer()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var fx = await SeedPublishedPipelineAsync(db);

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false).CloseAsync(fx.Requisition.Id);

        Assert.False(success);
        Assert.Contains("عرض", error);
        db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Published, (await db.RecruitmentRequisitions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Close_rejects_open_applications_and_cancels_interviews()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var fx = await SeedPublishedPipelineAsync(db);
        var offer = await db.OfferLetters.SingleAsync();
        offer.Status = OfferStatus.Revoked;
        await db.SaveChangesAsync();

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false).CloseAsync(fx.Requisition.Id);

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Closed, (await db.RecruitmentRequisitions.SingleAsync()).Status);
        var submitted = await db.RecruitmentApplications.SingleAsync(a => a.Id == fx.SubmittedApp);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, submitted.Status);
        Assert.Contains("إغلاق الشاغر", submitted.Notes);
        Assert.Equal(InterviewSlotStatus.Cancelled, (await db.RecruitmentInterviewSlots.SingleAsync()).Status);
        Assert.False((await db.PublicJobPostings.SingleAsync()).IsPublished);
    }

    [Fact]
    public void Freeze_rules_block_issued_or_unconverted_accepted_offers()
    {
        Assert.NotNull(EstablishmentRecruitmentCascadeRules.ValidateFreeze([new OfferLetter { Status = OfferStatus.Issued }]));
        Assert.NotNull(EstablishmentRecruitmentCascadeRules.ValidateFreeze([new OfferLetter { Status = OfferStatus.Accepted }]));
        Assert.Null(EstablishmentRecruitmentCascadeRules.ValidateFreeze(
            [new OfferLetter { Status = OfferStatus.Accepted, IsConvertedToEmployee = true }, new OfferLetter { Status = OfferStatus.Draft }]));
        Assert.Equal("تجميد المنصب: إعادة هيكلة", EstablishmentRecruitmentCascadeRules.FreezeCancellationReason(" إعادة هيكلة "));
    }

    internal sealed record PipelineFixture(
        RecruitmentRequisition Requisition,
        Guid SubmittedApp,
        Guid InterviewApp,
        Guid OfferApp,
        Guid WithdrawnApp,
        Guid OfferWorkflowId);

    /// <summary>Published requisition with posting, vacancy, an open/interview/offer/withdrawn application and an Issued offer.</summary>
    internal static async Task<PipelineFixture> SeedPublishedPipelineAsync(HRCoreDbContext db)
    {
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        var posting = new PublicJobPosting { Title = req.JobTitle, IsPublished = true, RequisitionId = req.Id };
        var vacancy = new InternalVacancy { Title = req.JobTitle, IsOpen = true, RequisitionId = req.Id, IsDerivedFromRequisition = true };
        db.PublicJobPostings.Add(posting);
        db.InternalVacancies.Add(vacancy);
        req.PublicJobPostingId = posting.Id;
        req.InternalVacancyId = vacancy.Id;

        var submitted = NewApp(req.Id, RecruitmentApplicationStatus.Submitted);
        var interview = NewApp(req.Id, RecruitmentApplicationStatus.InterviewScheduled);
        var offerApp = NewApp(req.Id, RecruitmentApplicationStatus.OfferExtended);
        var withdrawn = NewApp(req.Id, RecruitmentApplicationStatus.Withdrawn);
        db.RecruitmentApplications.AddRange(submitted, interview, offerApp, withdrawn);

        db.RecruitmentInterviewSlots.Add(new RecruitmentInterviewSlot
        {
            RecruitmentApplicationId = interview.Id,
            ScheduledAt = DateTime.UtcNow.AddDays(2),
            Status = InterviewSlotStatus.Scheduled
        });

        var workflow = new WorkflowInstance { EntityType = nameof(OfferLetter), Status = WorkflowStatus.InProgress };
        db.WorkflowInstances.Add(workflow);
        var offer = new OfferLetter
        {
            RecruitmentApplicationId = offerApp.Id,
            FundedPositionId = req.FundedPositionId,
            OfferNumber = $"OFF-{Guid.NewGuid():N}"[..12],
            Status = OfferStatus.Issued,
            OfferedSalary = 1000m,
            ProposedStartDate = DateTime.UtcNow.AddDays(30),
            WorkflowInstanceId = workflow.Id
        };
        db.OfferLetters.Add(offer);
        workflow.EntityId = offer.Id;

        await db.SaveChangesAsync();
        return new PipelineFixture(req, submitted.Id, interview.Id, offerApp.Id, withdrawn.Id, workflow.Id);
    }

    private static RecruitmentApplication NewApp(Guid requisitionId, RecruitmentApplicationStatus status) => new()
    {
        ApplicantFullName = $"مرشح {status}",
        AppliedPosition = "محلل",
        RequisitionId = requisitionId,
        AppliedAt = DateTime.UtcNow,
        Status = status,
        ConflictOfInterestDeclared = true
    };
}
