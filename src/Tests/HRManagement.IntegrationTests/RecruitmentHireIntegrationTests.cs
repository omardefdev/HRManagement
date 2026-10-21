using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Web.ViewModels;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class RecruitmentHireIntegrationTests
{
    [Fact]
    public async Task HireAndCreateEmployeeAsync_Uses_Requisition_JobTitle_And_OrgUnit()
    {
        await using var db = CreateContext();
        await RecruitmentLegalSourceTestSupport.CreateValidatorAsync(db);
        var org = new OrgUnit { Code = "OU-HIRE", NameAr = "دائرة HR", NameEn = "HR" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-HIRE",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-HIRE",
            FundedPositionId = position.Id,
            JobTitle = "محلل نظم",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.RecruitmentRequisitions.Add(req);
        var app = new RecruitmentApplication
        {
            ApplicantFullName = "أحمد علي",
            NationalId = "0101010101",
            RequisitionId = req.Id,
            Requisition = req,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.OfferAccepted
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            FundedPositionId = position.Id,
            OfferNumber = "OFF-HIRE",
            Status = OfferStatus.Accepted,
            OfferedSalary = 4500,
            ProposedStartDate = DateTime.UtcNow.AddDays(14),
            AcceptedDate = DateTime.UtcNow,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        app.ActiveOfferLetterId = offer.Id;
        app.ActiveOfferLetter = offer;
        await db.SaveChangesAsync();

        var integration = await CreateIntegrationAsync(db);
        var employee = await integration.HireAndCreateEmployeeAsync(
            app,
            "0101010101",
            "أحمد",
            null,
            "علي",
            DateTime.UtcNow.AddYears(-30),
            HRManagement.SharedKernel.Enums.Gender.Male,
            primaryOrgUnitId: null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        Assert.Equal(org.Id, employee.PrimaryOrgUnitId);
        var record = await db.EmploymentRecords.SingleAsync(e => e.EmployeeId == employee.Id);
        Assert.Equal("محلل نظم", record.PositionTitle);

        var reloadedPosition = await db.FundedPositions.AsNoTracking().SingleAsync(p => p.Id == position.Id);
        Assert.Equal(FundedPositionStatus.Occupied, reloadedPosition.Status);

        var vm = RecruitmentHireViewModel.FromApplication(app, req, position);
        Assert.Equal("REQ-HIRE", vm.RequisitionNumber);
        Assert.Equal("دائرة HR", vm.OrgUnitName);
    }

    private static async Task<RecruitmentIntegrationService> CreateIntegrationAsync(HRCoreDbContext db)
    {
        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:LegacyHireWithoutOffer"] = "false",
                ["Recruitment:RejectSiblingApplicationsOnHire"] = "true"
            })
            .Build();

        return new RecruitmentIntegrationService(
            await RecruitmentLegalSourceTestSupport.CreateHireClosureServiceAsync(
                db,
                publish.Object,
                config,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RecruitmentHireClosureService>.Instance));
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
