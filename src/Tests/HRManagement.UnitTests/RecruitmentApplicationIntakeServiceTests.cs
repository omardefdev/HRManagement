using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class RecruitmentApplicationIntakeServiceTests
{
    [Fact]
    public void ValidatePublishedRequisition_Rejects_NonPublished()
    {
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-1",
            Status = RequisitionStatus.Approved,
            JobTitle = "محلل"
        };
        var position = new FundedPosition { Status = FundedPositionStatus.Vacant };

        var error = RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(
            requisition, position, DateOnly.FromDateTime(DateTime.UtcNow), null);

        Assert.Equal("لا يمكن التقديم إلا على طلب شاغر منشور.", error);
    }

    [Fact]
    public void ValidatePublishedRequisition_Rejects_Closed()
    {
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-2",
            Status = RequisitionStatus.Published,
            ClosedDate = DateTime.UtcNow.AddDays(-1),
            JobTitle = "محلل"
        };
        var position = new FundedPosition { Status = FundedPositionStatus.Vacant };

        var error = RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(
            requisition, position, DateOnly.FromDateTime(DateTime.UtcNow), null);

        Assert.Equal("طلب الشاغر مغلق.", error);
    }

    [Fact]
    public void ValidatePublishedRequisition_Accepts_Published_Vacant()
    {
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-3",
            Status = RequisitionStatus.Published,
            JobTitle = "محلل"
        };
        var position = new FundedPosition { Status = FundedPositionStatus.Vacant };

        var error = RecruitmentApplicationIntakeRules.ValidatePublishedRequisition(
            requisition, position, DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));

        Assert.Null(error);
    }

    [Fact]
    public async Task GetOpenPublishedRequisitionsAsync_Returns_Published_Only()
    {
        await using var db = CreateContext();
        var position = SeedVacantPosition(db);
        await db.SaveChangesAsync();

        db.RecruitmentRequisitions.AddRange(
            new RecruitmentRequisition
            {
                RequisitionNumber = "REQ-PUB",
                FundedPositionId = position.Id,
                JobTitle = "منشور",
                Status = RequisitionStatus.Published,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1
            },
            new RecruitmentRequisition
            {
                // PLAN-26: a second *blocking* requisition on the same position is now rejected by the DB index.
                RequisitionNumber = "REQ-DRF",
                FundedPositionId = position.Id,
                JobTitle = "ملغى",
                Status = RequisitionStatus.Cancelled,
                RequestedDate = DateTime.UtcNow,
                VacancyCount = 1
            });
        await db.SaveChangesAsync();

        var intake = new RecruitmentApplicationIntakeService(db);
        var options = await intake.GetOpenPublishedRequisitionsAsync();

        Assert.Single(options);
        Assert.Equal("REQ-PUB", options[0].RequisitionNumber);
    }

    private static FundedPosition SeedVacantPosition(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-INTAKE", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-INTAKE",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        return position;
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
