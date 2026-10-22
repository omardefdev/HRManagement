using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class PublicCareersRequisitionGateTests
{
    [Fact]
    public async Task Index_lists_published_postings_without_ambient_tenant()
    {
        var tenantId = Guid.Parse("a0000001-0000-4000-8000-000000000001");
        await using var db = CreateContext(Tenant(null));
        var position = SeedVacantPosition(db);
        position.TenantId = tenantId;
        await db.SaveChangesAsync();
        var requisition = new RecruitmentRequisition
        {
            TenantId = tenantId,
            RequisitionNumber = "REQ-PUB",
            FundedPositionId = position.Id,
            JobTitle = "منسق علاقات عامة",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(requisition);
        db.PublicJobPostings.Add(new PublicJobPosting
        {
            TenantId = tenantId,
            Title = "منسق علاقات عامة",
            IsPublished = true,
            RequisitionId = requisition.Id,
            Requisition = requisition
        });
        await db.SaveChangesAsync();

        var controller = new PublicCareersController(
            db,
            Mock.Of<ICodeGenerator>(),
            new RecruitmentApplicationIntakeService(db),
            new RecruitmentInterviewService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new UnifiedIntakeService(db));
        var result = await controller.Index();
        var view = Assert.IsType<ViewResult>(result);
        var postings = Assert.IsAssignableFrom<IReadOnlyList<PublicJobPosting>>(view.Model);
        Assert.Contains(postings, p => p.Title == "منسق علاقات عامة");
    }

    [Fact]
    public async Task Index_Excludes_Approved_Only_Requisition_Postings()
    {
        await using var db = CreateContext();
        var position = SeedVacantPosition(db);
        await db.SaveChangesAsync();
        var legacy = new PublicJobPosting { Title = "Legacy", IsPublished = true };
        var approvedReq = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-APP",
            FundedPositionId = position.Id,
            JobTitle = "Approved only",
            Status = RequisitionStatus.Approved,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(approvedReq);
        var approvedPosting = new PublicJobPosting
        {
            Title = "Approved posting",
            IsPublished = true,
            RequisitionId = approvedReq.Id,
            Requisition = approvedReq
        };
        db.PublicJobPostings.AddRange(legacy, approvedPosting);
        await db.SaveChangesAsync();

        var controller = new PublicCareersController(
            db,
            Mock.Of<ICodeGenerator>(),
            new RecruitmentApplicationIntakeService(db),
            new RecruitmentInterviewService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new UnifiedIntakeService(db));
        var result = await controller.Index();
        var view = Assert.IsType<ViewResult>(result);
        var postings = Assert.IsAssignableFrom<IReadOnlyList<PublicJobPosting>>(view.Model);
        Assert.Contains(postings, p => p.Title == "Legacy");
        Assert.DoesNotContain(postings, p => p.Title == "Approved posting");
    }

    [Fact]
    public async Task Apply_Rejects_When_Requisition_Not_Published()
    {
        await using var db = CreateContext();
        var position = SeedVacantPosition(db);
        await db.SaveChangesAsync();
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-NP",
            FundedPositionId = position.Id,
            JobTitle = "Draft",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var posting = new PublicJobPosting
        {
            Title = "Hidden",
            IsPublished = true,
            RequisitionId = req.Id,
            Requisition = req
        };
        db.PublicJobPostings.Add(posting);
        await db.SaveChangesAsync();

        var codeGen = new Mock<ICodeGenerator>();
        var controller = new PublicCareersController(
            db,
            codeGen.Object,
            new RecruitmentApplicationIntakeService(db),
            new RecruitmentInterviewService(db, Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions())),
            new UnifiedIntakeService(db))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            }
        };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.HttpContext,
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

        var result = await controller.Apply(new HR.Core.Web.ViewModels.CitizenApplicationApplyViewModel
        {
            PublicJobPostingId = posting.Id,
            FullName = "Test",
            NationalId = "123"
        });

        Assert.IsType<RedirectToActionResult>(result);
        codeGen.Verify(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static FundedPosition SeedVacantPosition(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-GATE", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-GATE",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        return position;
    }

    private static ITenantContext Tenant(Guid? tenantId)
    {
        var mock = new Mock<ITenantContext>();
        mock.Setup(t => t.TenantId).Returns(tenantId);
        return mock.Object;
    }

    private static HRCoreDbContext CreateContext(ITenantContext? tenantContext = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options, tenantContext ?? Tenant(Guid.Empty));
        db.Database.EnsureCreated();
        return db;
    }
}
