using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class RecruitmentSelectionServiceTests
{
    [Fact]
    public async Task TransitionStatus_to_Assessed_is_rejected_and_status_unchanged()
    {
        // PLAN-26: «مُقيَّم» is reached only through RecruitmentScoringService (structured scores).
        await using var db = CreateContext();
        var (app, _) = await SeedApplicationAsync(db);

        var service = new RecruitmentSelectionService(db);
        var result = await service.TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Assessed, legacyHireWithoutOffer: false);

        Assert.False(result.Success);
        Assert.Equal(SelectionWorkflowRules.AssessedIsAutomaticMessage, result.Error);
        db.ChangeTracker.Clear();
        Assert.Equal(RecruitmentApplicationStatus.InterviewScheduled, (await db.RecruitmentApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task AllowedTargets_never_offer_Assessed()
    {
        await using var db = CreateContext();
        var (app, criteria) = await SeedApplicationAsync(db);
        app.AssessmentScore = 80m;
        app.CriteriaVersionId = criteria.Id;

        var targets = SelectionWorkflowRules.GetAllowedTargets(app);

        Assert.DoesNotContain(RecruitmentApplicationStatus.Assessed, targets);
        Assert.Contains(RecruitmentApplicationStatus.Rejected, targets);
    }

    private static async Task<(RecruitmentApplication app, RecruitmentAssessmentCriteria criteria)> SeedApplicationAsync(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-SEL", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-SEL",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-SEL",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var criteria = new RecruitmentAssessmentCriteria
        {
            RequisitionId = req.Id,
            VersionNumber = 1,
            Title = "v1",
            CriteriaJson = "[]"
        };
        db.RecruitmentAssessmentCriteria.Add(criteria);
        var app = new RecruitmentApplication
        {
            ApplicantFullName = "Test",
            AppliedPosition = "محلل",
            RequisitionId = req.Id,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.InterviewScheduled,
            ConflictOfInterestDeclared = true
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        return (app, criteria);
    }

    private static HRCoreDbContext CreateContext()
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
