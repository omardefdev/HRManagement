using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class Hcm026ClosingDateTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task Publish_without_closing_date_rejected()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Approved);
        req.ApplicationClosingDate = null;
        await db.SaveChangesAsync();
        var lifecycle = Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false);

        var (success, error) = await lifecycle.PublishAsync(req, Guid.NewGuid(), "hr");
        Assert.False(success);
        Assert.Contains("تاريخ إغلاق التقديم", error);

        var (internalSuccess, _) = await lifecycle.PublishInternalAsync(req, Guid.NewGuid(), "hr");
        Assert.False(internalSuccess);

        db.ChangeTracker.Clear();
        Assert.Equal(RequisitionStatus.Approved, (await db.RecruitmentRequisitions.SingleAsync()).Status);
        Assert.False(await db.PublicJobPostings.AnyAsync());
    }

    [Fact]
    public async Task Closing_date_before_min_posting_days_rejected()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Approved);
        req.ApplicationClosingDate = Today.AddDays(3);
        await db.SaveChangesAsync();

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false)
            .PublishAsync(req, Guid.NewGuid(), "hr");

        Assert.False(success);
        Assert.Contains("7", error);
    }

    [Fact]
    public async Task Closing_date_given_at_publish_is_used()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Approved);
        req.ApplicationClosingDate = null;
        await db.SaveChangesAsync();

        var (success, error) = await Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false)
            .PublishAsync(req, Guid.NewGuid(), "hr", applicationClosingDate: Today.AddDays(14));

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        Assert.Equal(Today.AddDays(14), (await db.RecruitmentRequisitions.SingleAsync()).ApplicationClosingDate);
    }

    [Fact]
    public async Task Posting_and_vacancy_get_the_closing_date()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Approved);
        var closing = Today.AddDays(20);
        req.ApplicationClosingDate = closing;
        await db.SaveChangesAsync();
        var lifecycle = Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false);

        Assert.True((await lifecycle.PublishAsync(req, Guid.NewGuid(), "hr")).Success);
        db.ChangeTracker.Clear();
        var published = await db.RecruitmentRequisitions.SingleAsync();
        Assert.True((await lifecycle.PublishInternalAsync(published, Guid.NewGuid(), "hr")).Success);

        db.ChangeTracker.Clear();
        Assert.Equal(closing, (await db.PublicJobPostings.SingleAsync()).ClosingDate);
        Assert.Equal(closing, (await db.InternalVacancies.SingleAsync()).ClosingDate);
    }

    [Fact]
    public void Mapping_copies_closing_date()
    {
        var req = new RecruitmentRequisition { JobTitle = "محلل", ApplicationClosingDate = Today.AddDays(9) };
        var position = new FundedPosition { GradeCode = "G5" };
        Assert.Equal(req.ApplicationClosingDate, RecruitmentRequisitionService.MapToPublicJobPosting(req, position).ClosingDate);
        Assert.Equal(req.ApplicationClosingDate, RecruitmentRequisitionService.MapToInternalVacancy(req, position).ClosingDate);
    }

    [Fact]
    public void Intake_rejected_after_closing_date_or_when_intake_closed()
    {
        var position = new FundedPosition { Status = FundedPositionStatus.Vacant };
        var open = new RecruitmentRequisition { Status = RequisitionStatus.Published, ApplicationClosingDate = Today };
        Assert.Null(RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(open, position, Today, null));

        var expired = new RecruitmentRequisition { Status = RequisitionStatus.Published, ApplicationClosingDate = Today.AddDays(-1) };
        Assert.NotNull(RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(expired, position, Today, null));

        var closed = new RecruitmentRequisition
        {
            Status = RequisitionStatus.Published,
            ApplicationClosingDate = Today.AddDays(10),
            IntakeClosedAt = DateTime.UtcNow
        };
        Assert.Equal("التقديم على هذا الشاغر مغلق.",
            RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(closed, position, Today, null));
    }

    [Fact]
    public async Task Intake_service_excludes_closed_requisitions()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var req = await Hcm026CriteriaServiceTests.SeedRequisitionAsync(db, RequisitionStatus.Published);
        var intake = new RecruitmentApplicationIntakeService(db);
        Assert.Single(await intake.GetOpenPublishedRequisitionsAsync());
        Assert.Null(await intake.ValidatePublishedRequisitionAsync(req.Id));

        req.IntakeClosedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Empty(await intake.GetOpenPublishedRequisitionsAsync());
        Assert.NotNull(await intake.ValidatePublishedRequisitionAsync(req.Id));
    }

    [Fact]
    public void Extension_rules()
    {
        var current = Today.AddDays(5);
        Assert.NotNull(RequisitionWorkflowRules.ValidateClosingDateExtension(current, Today.AddDays(4), Today, "تمديد لظروف طارئة"));
        Assert.NotNull(RequisitionWorkflowRules.ValidateClosingDateExtension(current, current, Today, "تمديد لظروف طارئة"));
        Assert.NotNull(RequisitionWorkflowRules.ValidateClosingDateExtension(current, Today.AddDays(9), Today, "قصير"));
        Assert.NotNull(RequisitionWorkflowRules.ValidateClosingDateExtension(null, Today.AddDays(-1), Today, "تمديد لظروف طارئة"));
        Assert.Null(RequisitionWorkflowRules.ValidateClosingDateExtension(current, Today.AddDays(9), Today, "تمديد لظروف طارئة"));
    }

    [Fact]
    public async Task Extension_reopens_closed_intake_and_audits()
    {
        await using var db = Hcm026CriteriaServiceTests.CreateContext();
        var fx = await Hcm026RequisitionIntegrityTests.SeedPublishedPipelineAsync(db);
        var req = fx.Requisition;
        req.ApplicationClosingDate = Today.AddDays(-2);
        req.IntakeClosedAt = DateTime.UtcNow.AddDays(-1);
        foreach (var p in await db.PublicJobPostings.ToListAsync()) p.IsPublished = false;
        foreach (var v in await db.InternalVacancies.ToListAsync()) v.IsOpen = false;
        await db.SaveChangesAsync();
        var lifecycle = Hcm026CriteriaServiceTests.CreateLifecycle(db, requireCriteria: false);

        Assert.False((await lifecycle.ExtendClosingDateAsync(req.Id, Today.AddDays(10), "قصير", null)).Success);

        var (success, error) = await lifecycle.ExtendClosingDateAsync(req.Id, Today.AddDays(10), "تمديد لاستقطاب مرشحين إضافيين", Guid.NewGuid());

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(Today.AddDays(10), saved.ApplicationClosingDate);
        Assert.Null(saved.IntakeClosedAt);
        Assert.Equal(RequisitionStatus.Published, saved.Status);
        var posting = await db.PublicJobPostings.SingleAsync();
        Assert.True(posting.IsPublished);
        Assert.Equal(Today.AddDays(10), posting.ClosingDate);
        var vacancy = await db.InternalVacancies.SingleAsync();
        Assert.True(vacancy.IsOpen);
        Assert.Equal(Today.AddDays(10), vacancy.ClosingDate);
        Assert.True(await db.AuditLogs.AnyAsync(l => l.EntityId == req.Id && l.Action == "ExtendApplicationClosingDate"));
    }
}
