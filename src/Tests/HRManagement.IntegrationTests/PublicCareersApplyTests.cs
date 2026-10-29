using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class PublicCareersApplyTests
{
    [Fact]
    public async Task Apply_Sets_RequisitionId_For_Derived_Posting()
    {
        await using var db = CreateContext();
        var (posting, _) = await SeedPublishedPostingAsync(db);

        var codeGen = new Mock<ICodeGenerator>();
        codeGen.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "CA-REF-001" });

        var controller = CreateController(db, codeGen.Object);
        var result = await controller.Apply(new HR.Core.Web.ViewModels.CitizenApplicationApplyViewModel
        {
            PublicJobPostingId = posting.Id,
            FullName = "Test Applicant",
            NationalId = "1234567890",
            // COM-030 (F2): the phone is validated and normalized now ("099" is not a phone number).
            Phone = "0999123456",
            Email = "t@test.sy"
        });

        Assert.IsType<RedirectToActionResult>(result);
        var saved = await db.CitizenApplications.SingleAsync();
        Assert.Equal(posting.RequisitionId, saved.RequisitionId);
    }

    [Fact]
    public async Task Apply_Rejects_Closed_Requisition()
    {
        await using var db = CreateContext();
        var (posting, req) = await SeedPublishedPostingAsync(db);
        req.ClosedDate = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();

        var controller = CreateController(db, Mock.Of<ICodeGenerator>());
        var result = await controller.Apply(new HR.Core.Web.ViewModels.CitizenApplicationApplyViewModel
        {
            PublicJobPostingId = posting.Id,
            FullName = "Test",
            NationalId = "999"
        });

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Empty(await db.CitizenApplications.ToListAsync());
    }

    private static PublicCareersController CreateController(HRCoreDbContext db, ICodeGenerator codeGen)
    {
        var controller = new PublicCareersController(
            db,
            codeGen,
            new RecruitmentApplicationIntakeService(db),
            new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new CandidatePublicStatusService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new HR.Core.Web.Services.CandidateDocumentService(
                db, new HR.Core.Web.Services.UnavailableCandidateDocumentUploadClient(), new HRManagement.ServiceDefaults.EfFileReferenceOutbox(db)),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
            new HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionIntakeService(db, new UnifiedIntakeService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())), new RecruitmentSelectionService(db), Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new HR.Core.Infrastructure.Services.Recruitment.Competitions.CompetitionResultsService(db, null!))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.HttpContext,
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
        return controller;
    }

    private static async Task<(PublicJobPosting posting, RecruitmentRequisition req)> SeedPublishedPostingAsync(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-APPLY", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-APPLY",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-APPLY",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var posting = new PublicJobPosting
        {
            Title = "Posting",
            IsPublished = true,
            RequisitionId = req.Id,
            Requisition = req
        };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();
        return (posting, req);
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
