using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Benefits;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>SYR-006: service regime runtime binding (SYR-001 ↔ SYR-003 ↔ HCM hire).</summary>
public class Syr006ServiceRegimeBindingTests
{
    private static async Task<(HRCoreDbContext Db, ServiceRegimeProvider RegimeProvider, ServiceRegimeValidator RegimeValidator)>
        CreateSeededContextAsync()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new HRCoreDbContext(options);
        await LegislationRegisterSeeder.SeedAsync(db);
        var legislationProvider = new LegislationRegisterProvider(db);
        await ServiceRegimeSeeder.SeedAsync(db, legislationProvider);
        var legalValidator = new RecruitmentLegalSourceValidator(legislationProvider);
        return (db, new ServiceRegimeProvider(db), new ServiceRegimeValidator(db, legalValidator));
    }

    [Fact]
    public async Task Seeder_resolves_law50_alias()
    {
        var (db, _, _) = await CreateSeededContextAsync();
        var law50 = await db.LegalRegimes.AsNoTracking().SingleAsync(r => r.LegalRegimeId == "law50");
        Assert.Equal(Syr001LegislationIds.Law50_2004, law50.ResolvedLegalSourceId);
    }

    [Fact]
    public void MapOfferToRegime_law50()
    {
        Assert.Equal("law50", LegalSourceToRegimeMapper.MapOfferToRegime(Syr001LegislationIds.Law50_2004));
        Assert.Equal("law53", LegalSourceToRegimeMapper.MapOfferToRegime(Syr001LegislationIds.Law53_2021));
        Assert.Null(LegalSourceToRegimeMapper.MapOfferToRegime("SYR-DEC-67-2026"));
    }

    [Fact]
    public async Task BenefitPlan_validator_rejects_unknown_legal_source()
    {
        var (_, _, validator) = await CreateSeededContextAsync();
        var error = await validator.ValidateBenefitPlanLegalSourceAsync("SYR-LAW-UNKNOWN");
        Assert.NotNull(error);
    }

    [Fact]
    public async Task Hire_sets_LegalRegimeId_on_employment_record()
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new HRCoreDbContext(options);
        await LegislationRegisterSeeder.SeedAsync(db);
        var legislationProvider = new LegislationRegisterProvider(db);
        await ServiceRegimeSeeder.SeedAsync(db, legislationProvider);
        var legalValidator = new RecruitmentLegalSourceValidator(legislationProvider);
        var regimeValidator = new ServiceRegimeValidator(db, legalValidator);
        var transitionService = new ServiceRegimeTransitionService(db, regimeValidator);
        var exceptionService = new ServiceRegimeExceptionService(db, regimeValidator, transitionService);

        var employee = new Employee
        {
            NationalId = "1111111111",
            FirstName = "Test",
            LastName = "User",
            FullName = "Test User",
            BirthDate = DateTime.UtcNow.AddYears(-30),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            HireDate = DateTime.UtcNow,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.PendingActivation
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            OfferNumber = "OFF-REGIME-1",
            Status = HR.Core.Domain.Enums.OfferStatus.Accepted,
            OfferedSalary = 1000,
            ProposedStartDate = DateTime.UtcNow,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };

        var closure = new RecruitmentHireClosureService(
            db,
            new EstablishmentService(),
            new FundedPositionVersionService(db),
            new RequisitionCloseService(),
            legalValidator,
            regimeValidator,
            exceptionService,
            new ReserveListService(
                db,
                Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()),
                NullLogger<ReserveListService>.Instance),
            new Moq.Mock<MassTransit.IPublishEndpoint>().Object,
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            NullLogger<RecruitmentHireClosureService>.Instance);

        var seedTask = typeof(RecruitmentHireClosureService)
            .GetMethod("SeedEmployeeSideEffectsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(closure, [employee, "محلل", (Guid?)null, offer, CancellationToken.None]) as Task;
        Assert.NotNull(seedTask);
        await seedTask!;

        await db.SaveChangesAsync();

        var record = await db.EmploymentRecords.SingleAsync(r => r.EmployeeId == employee.Id);
        Assert.Equal("law50", record.LegalRegimeId);
    }

    [Fact]
    public async Task Provider_resolve_by_legal_source_returns_law50()
    {
        var (_, provider, _) = await CreateSeededContextAsync();
        var regime = await provider.ResolveByLegalSourceIdAsync(Syr001LegislationIds.Law50_2004);
        Assert.NotNull(regime);
        Assert.Equal("law50", regime!.LegalRegimeId);
    }
}
