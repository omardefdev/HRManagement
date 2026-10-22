using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Controllers;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Legal;
using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class InternalVacancyPublishTests
{
    [Fact]
    public async Task PublishInternal_Creates_Linked_Vacancy_And_Sets_Requisition()
    {
        await using var db = CreateContext();
        await LegislationRegisterSeeder.SeedAsync(db);
        var (req, position) = await SeedApprovedInternalRequisitionAsync(db);

        var lifecycle = CreateLifecycle(db);
        var (success, error) = await lifecycle.PublishInternalAsync(req, null, null);

        Assert.True(success, error);
        db.ChangeTracker.Clear();

        var savedReq = await db.RecruitmentRequisitions
            .Include(r => r.InternalVacancy)
            .SingleAsync();
        Assert.Equal(RequisitionStatus.Published, savedReq.Status);
        Assert.NotNull(savedReq.InternalVacancyId);
        Assert.NotNull(savedReq.InternalVacancy);
        Assert.Equal(req.Id, savedReq.InternalVacancy!.RequisitionId);
        Assert.True(savedReq.InternalVacancy.IsDerivedFromRequisition);
        Assert.Equal(position.OrgUnitId, savedReq.InternalVacancy.OrgUnitId);
    }

    private static RecruitmentRequisitionLifecycleService CreateLifecycle(HRCoreDbContext db)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:Core.Recruitment"] = "Off" })
            .Build();
        var provider = new LegislationRegisterProvider(db);
        var validator = new RecruitmentLegalSourceValidator(provider);
        return new RecruitmentRequisitionLifecycleService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            validator,
            Mock.Of<IWorkflowStarter>(),
            config,
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions { RequireCriteriaBeforePublish = false }));
    }

    private static async Task<(RecruitmentRequisition Req, FundedPosition Position)> SeedApprovedInternalRequisitionAsync(
        HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-INT-PUB", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-INT-PUB",
            GradeCode = "G6",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-INT-PUB",
            FundedPositionId = position.Id,
            JobTitle = "أخصائي",
            Status = RequisitionStatus.Approved,
            PublishingChannel = RecruitmentPublishingChannel.InternalOnly,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004,
            ApplicationClosingDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30) // PLAN-26: required to publish
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();
        return (req, position);
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

[Collection("IntegrationSerial")]
public class EssTalentApplyScopeTests
{
    [Fact]
    public async Task ValidateInternalVacancy_Rejects_Unlinked_Legacy_Vacancy()
    {
        await using var db = CreateContext();
        var vacancy = new InternalVacancy
        {
            Title = "قديم",
            IsOpen = true
        };
        db.InternalVacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var intake = new RecruitmentApplicationIntakeService(db);
        var error = await intake.ValidateInternalVacancyAsync(vacancy.Id);

        Assert.NotNull(error);
    }

    [Fact]
    public async Task HasDuplicateInternalApplication_Blocks_Second_Application()
    {
        await using var db = CreateContext();
        var (req, vacancy, employee) = await SeedPublishedInternalAsync(db);

        db.InternalApplications.Add(new InternalApplication
        {
            InternalVacancyId = vacancy.Id,
            EmployeeId = employee.Id,
            RequisitionId = req.Id,
            Status = InternalApplicationStatus.Submitted
        });
        await db.SaveChangesAsync();

        var intake = new RecruitmentApplicationIntakeService(db);
        Assert.True(await intake.HasDuplicateInternalApplicationAsync(employee.Id, req.Id));
    }

    private static async Task<(RecruitmentRequisition Req, InternalVacancy Vacancy, Employee Employee)> SeedPublishedInternalAsync(
        HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-ESS", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-ESS",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-ESS",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            PublishingChannel = RecruitmentPublishingChannel.Both,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(req, position);
        db.InternalVacancies.Add(vacancy);
        req.InternalVacancy = vacancy;
        req.InternalVacancyId = vacancy.Id;
        var employee = new Employee
        {
            FullName = "موظف ESS",
            NationalId = "ESS-001",
            CurrentStatus = EmploymentStatus.Active,
            PrimaryOrgUnitId = org.Id
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return (req, vacancy, employee);
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

[Collection("IntegrationSerial")]
public class PromoteFromInternalTests
{
    [Fact]
    public async Task PromoteFromInternal_Creates_RecruitmentApplication_With_Source_Link()
    {
        await using var db = CreateContext();
        var (req, vacancy, internalApp) = await SeedInternalApplicationAsync(db);

        var controller = await RecruitmentTestControllers.CreateApplicationsController(db);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.HttpContext,
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

        var result = await controller.PromoteFromInternal(internalApp.Id);

        Assert.IsType<RedirectToActionResult>(result);
        var recruitment = await db.RecruitmentApplications.SingleAsync();
        Assert.Equal(req.Id, recruitment.RequisitionId);
        Assert.Equal(internalApp.Id, recruitment.SourceInternalApplicationId);
        // HCM-030 (A3): linking keeps the channel's stage (backfill mapping) instead of jumping to Shortlisted.
        Assert.Equal(RecruitmentApplicationStatus.UnderReview, recruitment.Status);
        Assert.Equal(RecruitmentIntakeChannel.Internal, recruitment.IntakeChannel);

        var reloadedInternal = await db.InternalApplications.SingleAsync();
        Assert.Equal(recruitment.Id, reloadedInternal.PromotedRecruitmentApplicationId);
    }

    private static async Task<(RecruitmentRequisition Req, InternalVacancy Vacancy, InternalApplication App)>
        SeedInternalApplicationAsync(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-PFI", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-PFI",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-PFI",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            PublishingChannel = RecruitmentPublishingChannel.InternalOnly,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);
        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(req, position);
        db.InternalVacancies.Add(vacancy);
        req.InternalVacancy = vacancy;
        req.InternalVacancyId = vacancy.Id;
        var employee = new Employee
        {
            FullName = "موظف داخلي",
            NationalId = "INT-001",
            CurrentStatus = EmploymentStatus.Active,
            PrimaryOrgUnitId = org.Id
        };
        db.Employees.Add(employee);
        var internalApp = new InternalApplication
        {
            InternalVacancyId = vacancy.Id,
            EmployeeId = employee.Id,
            RequisitionId = req.Id,
            Status = InternalApplicationStatus.UnderReview,
            AppliedAt = DateTime.UtcNow
        };
        db.InternalApplications.Add(internalApp);
        await db.SaveChangesAsync();
        return (req, vacancy, internalApp);
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

[Collection("IntegrationSerial")]
public class InternalHireClosureIntegrationTests
{
    [Fact]
    public async Task InternalHireClosure_VacatesPreviousPosition_AssignsTarget_PublishesPlacementEvent()
    {
        var (db, integration, publish) = await CreateEnvAsync();
        var (app, req, targetPosition, existingEmployee, previousPosition, offer) =
            await SeedInternalMobilityScenarioAsync(db);

        var employee = await integration.HireExistingEmployeeAsync(
            app,
            existingEmployee.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            "PERMANENT");

        var reloadedPrevious = await db.FundedPositions.AsNoTracking().SingleAsync(p => p.Id == previousPosition.Id);
        Assert.Equal(FundedPositionStatus.Vacant, reloadedPrevious.Status);
        Assert.Null(reloadedPrevious.OccupantEmployeeId);

        var reloadedTarget = await db.FundedPositions.AsNoTracking().SingleAsync(p => p.Id == targetPosition.Id);
        Assert.Equal(FundedPositionStatus.Occupied, reloadedTarget.Status);
        Assert.Equal(employee.Id, reloadedTarget.OccupantEmployeeId);

        var reloadedReq = await db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == req.Id);
        Assert.Equal(RequisitionStatus.Closed, reloadedReq.Status);

        // PLAN-29: internal mobility publishes IInternalPlacementCompleted only (no ICandidateAccepted → no Appointment).
        Assert.Equal(1, publish.Invocations.Count);
        Assert.Contains(
            publish.Invocations,
            i => i.Method.IsGenericMethod
                 && i.Method.GetGenericArguments().FirstOrDefault() == typeof(IInternalPlacementCompleted));
        Assert.DoesNotContain(
            publish.Invocations,
            i => i.Method.IsGenericMethod
                 && i.Method.GetGenericArguments().FirstOrDefault() == typeof(ICandidateAccepted));
        Assert.DoesNotContain(
            publish.Invocations,
            i => i.Method.IsGenericMethod
                 && i.Method.GetGenericArguments().FirstOrDefault() == typeof(IEmployeeCreated));
    }

    private static async Task<(HRCoreDbContext db, RecruitmentIntegrationService integration, Mock<IPublishEndpoint> publish)> CreateEnvAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Recruitment:RejectSiblingApplicationsOnHire"] = "true",
                ["Recruitment:LegacyHireWithoutOffer"] = "false"
            })
            .Build();

        var publish = new Mock<IPublishEndpoint>();
        publish.Setup(p => p.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publish.Setup(p => p.Publish(It.IsAny<ICandidateAccepted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publish.Setup(p => p.Publish(It.IsAny<IEmployeeCreated>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publish.Setup(p => p.Publish(It.IsAny<IInternalPlacementCompleted>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var closure = await RecruitmentLegalSourceTestSupport.CreateHireClosureServiceAsync(
            db,
            publish.Object,
            config,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RecruitmentHireClosureService>.Instance);
        var integration = new RecruitmentIntegrationService(closure);
        return (db, integration, publish);
    }

    private static async Task<(
        RecruitmentApplication App,
        RecruitmentRequisition Req,
        FundedPosition TargetPosition,
        Employee ExistingEmployee,
        FundedPosition PreviousPosition,
        OfferLetter Offer)> SeedInternalMobilityScenarioAsync(HRCoreDbContext db)
    {
        var orgFrom = new OrgUnit { Code = "OU-FROM", NameAr = "من", NameEn = "From" };
        var orgTo = new OrgUnit { Code = "OU-TO", NameAr = "إلى", NameEn = "To" };
        db.OrgUnits.AddRange(orgFrom, orgTo);

        var previousPosition = new FundedPosition
        {
            PositionCode = "POS-OLD",
            GradeCode = "G4",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Occupied,
            OrgUnitId = orgFrom.Id
        };
        var targetPosition = new FundedPosition
        {
            PositionCode = "VAC-INT-HIRE",
            GradeCode = "G6",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = orgTo.Id
        };
        db.FundedPositions.AddRange(previousPosition, targetPosition);

        var existingEmployee = new Employee
        {
            FullName = "موظف موجود",
            NationalId = "INT-HIRE-1",
            CurrentStatus = EmploymentStatus.Active,
            PrimaryOrgUnitId = orgFrom.Id
        };
        db.Employees.Add(existingEmployee);
        previousPosition.OccupantEmployeeId = existingEmployee.Id;

        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-INT-HIRE",
            FundedPositionId = targetPosition.Id,
            JobTitle = "أخصائي",
            Status = RequisitionStatus.Published,
            PublishingChannel = RecruitmentPublishingChannel.InternalOnly,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(req);

        var vacancy = RecruitmentRequisitionService.MapToInternalVacancy(req, targetPosition);
        db.InternalVacancies.Add(vacancy);
        req.InternalVacancy = vacancy;
        req.InternalVacancyId = vacancy.Id;

        var internalApp = new InternalApplication
        {
            InternalVacancyId = vacancy.Id,
            EmployeeId = existingEmployee.Id,
            RequisitionId = req.Id,
            Status = InternalApplicationStatus.Shortlisted,
            AppliedAt = DateTime.UtcNow
        };
        db.InternalApplications.Add(internalApp);

        var app = new RecruitmentApplication
        {
            ApplicantFullName = existingEmployee.FullName,
            NationalId = existingEmployee.NationalId,
            RequisitionId = req.Id,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.OfferAccepted,
            SourceInternalApplicationId = internalApp.Id
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            RecruitmentApplicationId = app.Id,
            FundedPositionId = targetPosition.Id,
            OfferNumber = "OFF-INT-1",
            Status = OfferStatus.Accepted,
            OfferedSalary = 6000,
            ProposedStartDate = DateTime.UtcNow.AddDays(30),
            AcceptedDate = DateTime.UtcNow,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();

        app.ActiveOfferLetterId = offer.Id;
        await db.SaveChangesAsync();
        return (app, req, targetPosition, existingEmployee, previousPosition, offer);
    }
}
