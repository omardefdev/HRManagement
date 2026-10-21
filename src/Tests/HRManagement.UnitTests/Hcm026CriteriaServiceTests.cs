using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.UnitTests;

public class Hcm026CriteriaServiceTests
{
    private static readonly AssessmentCriteriaItem[] TwoItems =
    [
        new("interview", "المقابلة", 40m, 100m, AssessmentStage.Interview),
        new("exp", "الخبرة", 60m, 50m, AssessmentStage.Experience)
    ];

    [Fact]
    public async Task First_version_is_created_active()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, RequisitionStatus.Draft);
        var service = new RecruitmentAssessmentCriteriaService(db);

        var (criteria, error) = await service.CreateVersionAsync(req.Id, "v1", TwoItems, null, Guid.NewGuid(), false);

        Assert.Null(error);
        Assert.NotNull(criteria);
        Assert.Equal(1, criteria.VersionNumber);
        Assert.True(criteria.IsActive);
        Assert.Equal(TwoItems, AssessmentCriteriaSerializer.Deserialize(criteria.CriteriaJson));
        Assert.Equal(criteria.Id, (await service.GetActiveAsync(req.Id))?.Id);
    }

    [Fact]
    public async Task Second_version_deactivates_first()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, RequisitionStatus.Approved);
        var service = new RecruitmentAssessmentCriteriaService(db);

        var (v1, _) = await service.CreateVersionAsync(req.Id, null, TwoItems, null, null, false);
        var (v2, error) = await service.CreateVersionAsync(req.Id, null, TwoItems, "تعديل الأوزان", null, false);

        Assert.Null(error);
        Assert.Equal(2, v2!.VersionNumber);
        db.ChangeTracker.Clear();
        var versions = await service.GetVersionsAsync(req.Id);
        Assert.Equal(2, versions.Count);
        Assert.Single(versions, v => v.IsActive);
        Assert.False(versions.Single(v => v.Id == v1!.Id).IsActive);
        Assert.True(versions.Single(v => v.Id == v2.Id).IsActive);
    }

    [Fact]
    public async Task Invalid_items_rejected()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, RequisitionStatus.Draft);
        var service = new RecruitmentAssessmentCriteriaService(db);

        var (criteria, error) = await service.CreateVersionAsync(
            req.Id, null, [new("a", "أ", 90m, 100m, AssessmentStage.Exam)], null, null, false);

        Assert.Null(criteria);
        Assert.NotNull(error);
        Assert.False(await db.RecruitmentAssessmentCriteria.AnyAsync());
    }

    [Theory]
    [InlineData(RequisitionStatus.Closed)]
    [InlineData(RequisitionStatus.Cancelled)]
    public async Task Closed_or_cancelled_requisition_rejected(RequisitionStatus status)
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, status);
        var service = new RecruitmentAssessmentCriteriaService(db);

        var (criteria, error) = await service.CreateVersionAsync(req.Id, null, TwoItems, null, null, true);

        Assert.Null(criteria);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task New_version_after_lock_requires_reason_and_approve()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, RequisitionStatus.Published);
        var service = new RecruitmentAssessmentCriteriaService(db);
        var (v1, _) = await service.CreateVersionAsync(req.Id, null, TwoItems, null, null, false);
        Assert.True((await service.LockAsync(v1!.Id)).Success);

        var noReason = await service.CreateVersionAsync(req.Id, null, TwoItems, null, null, true);
        Assert.Null(noReason.Criteria);
        Assert.NotNull(noReason.Error);

        var shortReason = await service.CreateVersionAsync(req.Id, null, TwoItems, "قصير", null, true);
        Assert.Null(shortReason.Criteria);

        var noApprove = await service.CreateVersionAsync(req.Id, null, TwoItems, "تصحيح وزن بند الخبرة", null, false);
        Assert.Null(noApprove.Criteria);
        Assert.NotNull(noApprove.Error);

        var approver = Guid.NewGuid();
        var ok = await service.CreateVersionAsync(req.Id, null, TwoItems, "تصحيح وزن بند الخبرة", approver, true);
        Assert.Null(ok.Error);
        Assert.Equal(2, ok.Criteria!.VersionNumber);
        Assert.Equal(approver, ok.Criteria.ApprovedBy);

        db.ChangeTracker.Clear();
        var locked = await db.RecruitmentAssessmentCriteria.SingleAsync(c => c.Id == v1.Id);
        Assert.False(locked.IsActive);
        Assert.NotNull(locked.LockedAt);
        Assert.True(await db.AuditLogs.AnyAsync(l => l.EntityId == ok.Criteria.Id && l.Action == "ReplaceLockedCriteriaVersion"));
    }

    [Fact]
    public async Task Publish_blocked_without_active_criteria()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, RequisitionStatus.Approved);
        var lifecycle = CreateLifecycle(db, requireCriteria: true);

        var (success, error) = await lifecycle.PublishAsync(req, Guid.NewGuid(), "hr");
        Assert.False(success);
        Assert.Equal(AssessmentCriteriaRules.RequiredBeforePublishMessage, error);

        var (internalSuccess, internalError) = await lifecycle.PublishInternalAsync(req, Guid.NewGuid(), "hr");
        Assert.False(internalSuccess);
        Assert.Equal(AssessmentCriteriaRules.RequiredBeforePublishMessage, internalError);

        await new RecruitmentAssessmentCriteriaService(db).CreateVersionAsync(req.Id, null, TwoItems, null, null, false);
        db.ChangeTracker.Clear();
        var (afterCriteria, afterError) = await lifecycle.PublishAsync(req, Guid.NewGuid(), "hr");
        Assert.True(afterCriteria, afterError);
    }

    [Fact]
    public async Task Publish_allowed_without_criteria_when_option_disabled()
    {
        await using var db = CreateContext();
        var req = await SeedRequisitionAsync(db, RequisitionStatus.Approved);
        var lifecycle = CreateLifecycle(db, requireCriteria: false);

        var (success, error) = await lifecycle.PublishAsync(req, Guid.NewGuid(), "hr");
        Assert.True(success, error);
        Assert.Equal(RequisitionStatus.Published, (await db.RecruitmentRequisitions.SingleAsync()).Status);
    }

    internal static RecruitmentRequisitionLifecycleService CreateLifecycle(HRCoreDbContext db, bool requireCriteria)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();
        return new RecruitmentRequisitionLifecycleService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            Mock.Of<IWorkflowStarter>(),
            config,
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions { RequireCriteriaBeforePublish = requireCriteria }));
    }

    internal static async Task<RecruitmentRequisition> SeedRequisitionAsync(HRCoreDbContext db, RequisitionStatus status)
    {
        if (!await db.LegislationSources.AnyAsync())
            await LegislationRegisterSeeder.SeedAsync(db);

        var org = new OrgUnit { Code = $"OU-{Guid.NewGuid():N}"[..12], NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = $"VAC-{Guid.NewGuid():N}"[..12],
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = $"REQ-{Guid.NewGuid():N}"[..12],
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = status,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004,
            PublishingChannel = RecruitmentPublishingChannel.Both,
            // PLAN-26 session 2: a closing date is required to publish.
            ApplicationClosingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30)
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();
        return req;
    }

    internal static HRCoreDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
