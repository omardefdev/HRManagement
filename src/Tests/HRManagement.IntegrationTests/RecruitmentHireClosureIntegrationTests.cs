using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class RecruitmentHireClosureIntegrationTests
{
    [Fact]
    public async Task HireClosure_AssignsPosition_ClosesRequisition_ConvertsOffer_PublishesV3Events()
    {
        var (db, integration, publish) = await CreateEnvAsync();
        var (app, req, position, offer) = await SeedOfferAcceptedScenarioAsync(db);

        var employee = await integration.HireAndCreateEmployeeAsync(
            app,
            "0101010101",
            "أحمد",
            null,
            "علي",
            DateTime.UtcNow.AddYears(-30),
            Gender.Male,
            primaryOrgUnitId: null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var reloadedPosition = await db.FundedPositions.AsNoTracking().SingleAsync(p => p.Id == position.Id);
        Assert.Equal(FundedPositionStatus.Occupied, reloadedPosition.Status);
        Assert.Equal(employee.Id, reloadedPosition.OccupantEmployeeId);

        var reloadedReq = await db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == req.Id);
        Assert.Equal(RequisitionStatus.Closed, reloadedReq.Status);
        Assert.Equal(0, reloadedReq.VacancyCount);
        Assert.NotNull(reloadedReq.ClosedDate);

        var reloadedOffer = await db.OfferLetters.AsNoTracking().SingleAsync(o => o.Id == offer.Id);
        Assert.True(reloadedOffer.IsConvertedToEmployee);

        var reloadedApp = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id);
        Assert.Equal(RecruitmentApplicationStatus.Hired, reloadedApp.Status);
        Assert.Equal(employee.Id, reloadedApp.HiredEmployeeId);

        var employmentRecord = await db.EmploymentRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == employee.Id);
        Assert.Equal("law50", employmentRecord.LegalRegimeId);

        // ICandidateAccepted + IEmployeeCreated, then (HCM-032, PLAN-32) the requisition-closed aggregate because the hire
        // filled the last vacancy. QA-034: the count of 2 predated HCM-032; the exact message list is asserted instead.
        Assert.Equal(
            [typeof(ICandidateAccepted), typeof(IEmployeeCreated), typeof(IRecruitmentRequisitionClosed)],
            publish.Invocations
                .Where(i => i.Method.Name == nameof(IPublishEndpoint.Publish) && i.Method.IsGenericMethod)
                .Select(i => i.Method.GetGenericArguments()[0]));
        Assert.Equal(3, publish.Invocations.Count); // nothing else was called on the endpoint (review B-1)
    }

    [Fact]
    public async Task HireClosure_UnmappedLegalSource_EnqueuesException_AndNullRegime()
    {
        var (db, integration, _) = await CreateEnvAsync();
        var (app, _, _, _) = await SeedOfferAcceptedScenarioAsync(db, legalSourceId: "SYR-DEC-67-2026");

        var employee = await integration.HireAndCreateEmployeeAsync(
            app,
            "0707070707",
            "ليلى",
            null,
            "حسن",
            DateTime.UtcNow.AddYears(-29),
            Gender.Female,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var record = await db.EmploymentRecords.AsNoTracking()
            .SingleAsync(r => r.EmployeeId == employee.Id);
        Assert.Null(record.LegalRegimeId);

        var exception = await db.ServiceRegimeExceptions.AsNoTracking()
            .SingleAsync(e => e.EmployeeId == employee.Id);
        Assert.Equal(ServiceRegimeExceptionStatus.Open, exception.Status);
        Assert.Equal(ServiceRegimeExceptionType.UnclassifiedRegime, exception.ExceptionType);
    }

    [Fact]
    public async Task HireClosure_ThrowsWhenOfferMissingLegalSource()
    {
        var (db, integration, _) = await CreateEnvAsync();
        var (app, _, _, _) = await SeedOfferAcceptedScenarioAsync(db, includeLegalSource: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            integration.HireAndCreateEmployeeAsync(
                app,
                "0606060606",
                "سامي",
                null,
                "رضا",
                DateTime.UtcNow.AddYears(-31),
                Gender.Male,
                null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                "PERMANENT"));

        Assert.Equal(RecruitmentLegalSourceRules.RequiredOnHireMessage, ex.Message);
    }

    [Fact]
    public async Task HireClosure_RejectsSiblingApplications_WhenConfigured()
    {
        var (db, integration, _) = await CreateEnvAsync(rejectSiblings: true);
        var (app, req, _, offer) = await SeedOfferAcceptedScenarioAsync(db);

        // HCM-027 (F2): a Ranked sibling now goes to the reserve list; unranked open siblings are still rejected.
        var sibling = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح آخر",
            RequisitionId = req.Id,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Ranked,
            Rank = 2
        };
        var unranked = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح غير مرتب",
            RequisitionId = req.Id,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Assessed
        };
        db.RecruitmentApplications.AddRange(sibling, unranked);
        await db.SaveChangesAsync();

        await integration.HireAndCreateEmployeeAsync(
            app,
            "0202020202",
            "سارة",
            null,
            "محمد",
            DateTime.UtcNow.AddYears(-28),
            Gender.Female,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var savedSibling = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == sibling.Id);
        Assert.Equal(RecruitmentApplicationStatus.ReserveListed, savedSibling.Status);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(6), savedSibling.ReserveUntil);
        var unrankedStatus = await db.RecruitmentApplications.AsNoTracking()
            .Where(a => a.Id == unranked.Id)
            .Select(a => a.Status)
            .SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Rejected, unrankedStatus);
    }

    [Fact]
    public async Task HireClosure_LeavesSiblingApplicationsUnchanged_WhenSiblingHandlingOff()
    {
        var (db, integration, _) = await CreateEnvAsync(rejectSiblings: false);
        var (app, req, _, _) = await SeedOfferAcceptedScenarioAsync(db);
        var sibling = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح آخر",
            RequisitionId = req.Id,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Ranked,
            Rank = 2
        };
        db.RecruitmentApplications.Add(sibling);
        await db.SaveChangesAsync();

        await integration.HireAndCreateEmployeeAsync(
            app,
            "0606060606",
            "هدى",
            null,
            "سالم",
            DateTime.UtcNow.AddYears(-26),
            Gender.Female,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == sibling.Id);
        Assert.Equal(RecruitmentApplicationStatus.Ranked, saved.Status);
        Assert.Null(saved.ReserveUntil);
    }

    [Fact]
    public async Task HireClosure_SecondAttempt_ThrowsWhenOfferAlreadyConverted()
    {
        var (db, integration, _) = await CreateEnvAsync();
        var (app, _, _, _) = await SeedOfferAcceptedScenarioAsync(db);

        await integration.HireAndCreateEmployeeAsync(
            app,
            "0303030303",
            "خالد",
            null,
            "سعد",
            DateTime.UtcNow.AddYears(-32),
            Gender.Male,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var freshApp = await db.RecruitmentApplications
            .Include(a => a.Requisition)
                .ThenInclude(r => r!.FundedPosition)
            .Include(a => a.ActiveOfferLetter)
            .SingleAsync(a => a.Id == app.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            integration.HireAndCreateEmployeeAsync(
                freshApp,
                "0404040404",
                "ليلى",
                null,
                "عمر",
                DateTime.UtcNow.AddYears(-27),
                Gender.Female,
                null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                "PERMANENT"));
    }

    [Fact]
    public async Task HireClosure_Idempotent_ReturnsExistingEmployeeWhenAlreadyHired()
    {
        var (db, integration, _) = await CreateEnvAsync();
        var (app, _, _, _) = await SeedOfferAcceptedScenarioAsync(db);

        var first = await integration.HireAndCreateEmployeeAsync(
            app,
            "0505050505",
            "نور",
            null,
            "حسن",
            DateTime.UtcNow.AddYears(-29),
            Gender.Female,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var second = await integration.HireExistingEmployeeAsync(
            app,
            first.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await db.Employees.CountAsync());
    }

    private static async Task<(HRCoreDbContext db, RecruitmentIntegrationService integration, Mock<IPublishEndpoint> publish)> CreateEnvAsync(
        bool rejectSiblings = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publish.Setup(p => p.Publish(It.IsAny<ICandidateAccepted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publish.Setup(p => p.Publish(It.IsAny<IEmployeeCreated>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:RejectSiblingApplicationsOnHire"] = rejectSiblings.ToString(),
                ["Recruitment:LegacyHireWithoutOffer"] = "false"
            })
            .Build();

        var closure = await RecruitmentLegalSourceTestSupport.CreateHireClosureServiceAsync(
            db,
            publish.Object,
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecruitmentHireClosureService>.Instance);
        var integration = new RecruitmentIntegrationService(closure);
        return (db, integration, publish);
    }

    private static async Task<(RecruitmentApplication App, RecruitmentRequisition Req, FundedPosition Position, OfferLetter Offer)>
        SeedOfferAcceptedScenarioAsync(HRCoreDbContext db, bool includeLegalSource = true, string? legalSourceId = null)
    {
        var org = new OrgUnit { Code = "OU-CLOSE", NameAr = "دائرة HR", NameEn = "HR" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-CLOSE",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);

        var posting = new PublicJobPosting
        {
            Title = "محلل نظم",
            IsPublished = true
        };
        db.PublicJobPostings.Add(posting);

        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-CLOSE",
            FundedPositionId = position.Id,
            JobTitle = "محلل نظم",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            PublicJobPosting = posting,
            PublicJobPostingId = posting.Id,
            LegalSourceId = includeLegalSource
                ? (legalSourceId ?? Syr001LegislationIds.Law50_2004)
                : null
        };
        posting.RequisitionId = req.Id;
        db.RecruitmentRequisitions.Add(req);
        posting.RequisitionId = req.Id;

        var app = new RecruitmentApplication
        {
            ApplicantFullName = "أحمد علي",
            // PLAN-29: no national id on the application, so the distinct ids typed by each test are not compared
            // with it (the comparison is covered by Hcm029HireErrorsTests).
            RequisitionId = req.Id,
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
            OfferNumber = "OFF-CLOSE-1",
            Status = OfferStatus.Accepted,
            OfferedSalary = 5000,
            ProposedStartDate = DateTime.UtcNow.AddDays(30),
            AcceptedDate = DateTime.UtcNow,
            LegalSourceId = includeLegalSource
                ? (legalSourceId ?? Syr001LegislationIds.Law50_2004)
                : null
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();

        app.ActiveOfferLetterId = offer.Id;
        await db.SaveChangesAsync();
        return (app, req, position, offer);
    }
}
