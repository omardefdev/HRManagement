using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>SYR-009: Article 159, separation regime binding, promotion, delegation lifecycle.</summary>
public class Syr009RegimeLifecycleTests
{
    private static async Task<(HRCoreDbContext Db, ServiceRegimeExceptionService ExceptionService)>
        CreateGovernanceContextAsync()
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
        return (db, exceptionService);
    }

    [Fact]
    public async Task Article159_cross_to_special_enqueues_exception()
    {
        var (db, exceptionService) = await CreateGovernanceContextAsync();
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = employeeId,
            NationalId = "4444444444",
            FirstName = "Art",
            LastName = "159",
            FullName = "Art 159",
            BirthDate = DateTime.UtcNow.AddYears(-35),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            HireDate = DateTime.UtcNow,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active
        });
        var recordId = Guid.NewGuid();
        db.EmploymentRecords.Add(new EmploymentRecord
        {
            Id = recordId,
            EmployeeId = employeeId,
            PositionTitle = "موظف",
            StartDate = DateTime.UtcNow,
            EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType.Permanent,
            Status = HRManagement.SharedKernel.Enums.EmploymentStatus.Active.ToString(),
            LegalRegimeId = "law50"
        });
        await db.SaveChangesAsync();

        var conflict = ServiceRegimeArticle159Rules.DetectConflict("law50", "special_banking");
        Assert.NotNull(conflict);

        var entry = await exceptionService.EnqueueArticle159Async(
            employeeId,
            recordId,
            "law50",
            "special_banking",
            conflict!.EntitlementsJson,
            ServiceRegimeExceptionSource.Transition);

        Assert.NotNull(entry);
        Assert.Equal(ServiceRegimeExceptionType.Article159Conflict, entry!.ExceptionType);

        var resolveError = await exceptionService.ResolveArticle159Async(
            entry.Id,
            legalDecisionId: null!,
            resolution: "reviewed",
            resolvedBy: Guid.NewGuid(),
            conflictNotes: "reviewed");
        Assert.Equal(ServiceRegimeArticle159Rules.LegalDecisionRequiredMessage, resolveError);
    }

    [Fact]
    public void Separation_law53_rejects_invalid_type()
    {
        var error = ServiceRegimeSeparationRules.ValidateSeparationType("law53", "retirement");
        Assert.Equal(ServiceRegimeSeparationRules.SeparationTypeNotAllowedMessage, error);
    }

    [Fact]
    public void SeparationCompleted_v3_carries_payroll_regime()
    {
        ISeparationCompleted message = new TestSeparationCompleted();
        Assert.Equal(3, message.SchemaVersion);
        Assert.Equal(ServiceRegimeToPayrollMapper.PayrollLaw53, message.PayrollRegimeCode);
        Assert.Equal("law53", message.SourceLegalRegimeId);
    }

    private sealed class TestSeparationCompleted : ISeparationCompleted
    {
        public Guid EmployeeId { get; init; }
        public string SeparationType { get; init; } = "Resignation";
        public DateTime EffectiveDate { get; init; } = DateTime.UtcNow;
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
        public string? CorrelationId { get; init; }
        public string SourceSystem { get; init; } = "test";
        public Guid? ContractId { get; init; }
        public Guid? SeparationRecordId { get; init; }
        public string? SourceLegalRegimeId { get; init; } = "law53";
        public string? PayrollRegimeCode { get; init; } = ServiceRegimeToPayrollMapper.PayrollLaw53;
    }

    [Fact]
    public async Task EOS_uses_law53_config_when_regime_law53()
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new PayrollDbContext(options);
        var law53 = PayrollRegimeSeed.CreateLaw53Configuration(isActive: false);
        db.PayrollConfigurations.Add(law53);
        await db.SaveChangesAsync();

        var repo = new PayrollRepository(db);
        var config = await repo.GetConfigurationByRegimeCodeAsync(ServiceRegimeToPayrollMapper.PayrollLaw53, CancellationToken.None);

        Assert.NotNull(config);
        Assert.Equal("LAW_53_2021", config!.RegimeCode);
    }

    [Fact]
    public void Promotion_requires_decision_ref()
    {
        var error = ServiceRegimePromotionRules.ValidatePromotion("law50", 3, 4, 1, 2, null);
        Assert.Equal(ServiceRegimePromotionRules.DecisionRefRequiredMessage, error);
    }

    [Fact]
    public void Delegation_preserves_regime()
    {
        Assert.True(ServiceRegimeMovementRules.PreservesRegime("delegation"));
        Assert.Null(ServiceRegimeMovementRules.ValidateRegimeChangeFields("delegation", null, null));
    }
}
