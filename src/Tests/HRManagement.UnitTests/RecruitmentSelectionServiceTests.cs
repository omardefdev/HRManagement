using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class RecruitmentSelectionServiceTests
{
    [Fact]
    public async Task RecordAssessment_Snapshots_CriteriaVersion()
    {
        await using var db = CreateContext();
        var (app, criteria) = await SeedApplicationAsync(db);

        var service = new RecruitmentSelectionService(db);
        var result = await service.RecordAssessmentAsync(app.Id, criteria.Id, 88m);

        Assert.True(result.Success);
        var saved = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(criteria.Id, saved.CriteriaVersionId);
        Assert.Equal(88m, saved.AssessmentScore);
        Assert.Equal(RecruitmentApplicationStatus.Assessed, saved.Status);
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
