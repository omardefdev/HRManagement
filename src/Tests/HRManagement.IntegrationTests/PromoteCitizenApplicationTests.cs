using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class PromoteCitizenApplicationTests
{
    [Fact]
    public async Task PromoteFromCitizen_Creates_RecruitmentApplication_With_Same_RequisitionId()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-PROM", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-PROM",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-PROM",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var posting = new PublicJobPosting { Title = "P", IsPublished = true, RequisitionId = req.Id };
        db.PublicJobPostings.Add(posting);
        var citizen = new CitizenApplication
        {
            PublicJobPostingId = posting.Id,
            RequisitionId = req.Id,
            ReferenceNumber = "CA-PROM-1",
            FullName = "مرشح",
            NationalId = "111",
            Status = CitizenApplicationStatus.Shortlisted,
            SubmittedAt = DateTime.UtcNow
        };
        db.CitizenApplications.Add(citizen);
        await db.SaveChangesAsync();

        var controller = await RecruitmentTestControllers.CreateApplicationsController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.HttpContext,
            Moq.Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

        var result = await controller.PromoteFromCitizen(citizen.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var recruitment = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(req.Id, recruitment.RequisitionId);
        Assert.Equal(RecruitmentApplicationStatus.Shortlisted, recruitment.Status);
        Assert.Equal(recruitment.Id, (await db.CitizenApplications.SingleAsync()).PromotedRecruitmentApplicationId);
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
