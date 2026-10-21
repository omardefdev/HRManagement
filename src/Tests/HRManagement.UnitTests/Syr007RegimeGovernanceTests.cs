using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Moq;
using Payroll.Application.Services;
using Payroll.Domain.Enums;

namespace HRManagement.UnitTests;

/// <summary>SYR-007: exception queue, regime transitions, and payroll bridge.</summary>
public class Syr007RegimeGovernanceTests
{
    private static async Task<(HRCoreDbContext Db, ServiceRegimeExceptionService ExceptionService, ServiceRegimeTransitionService TransitionService)>
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
        return (db, exceptionService, transitionService);
    }

    [Fact]
    public async Task Hire_unmapped_legal_source_enqueues_exception()
    {
        var (db, exceptionService, _) = await CreateGovernanceContextAsync();
        var employee = new Employee
        {
            NationalId = "2222222222",
            FirstName = "Test",
            LastName = "Unmapped",
            FullName = "Test Unmapped",
            BirthDate = DateTime.UtcNow.AddYears(-28),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            HireDate = DateTime.UtcNow,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.PendingActivation
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var record = new EmploymentRecord
        {
            EmployeeId = employee.Id,
            PositionTitle = "محلل",
            StartDate = DateTime.UtcNow,
            EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType.Permanent,
            Status = HRManagement.SharedKernel.Enums.EmploymentStatus.PendingActivation.ToString(),
            LegalRegimeId = null
        };
        db.EmploymentRecords.Add(record);
        await db.SaveChangesAsync();

        var entry = await exceptionService.EnqueueIfUnclassifiedAsync(
            employee,
            record,
            ServiceRegimeExceptionSource.Hire,
            "SYR-DEC-67-2026");

        Assert.NotNull(entry);
        Assert.Equal(ServiceRegimeExceptionStatus.Open, entry!.Status);
        Assert.Equal(ServiceRegimeExceptionType.UnclassifiedRegime, entry.ExceptionType);
    }

    [Fact]
    public async Task Transition_law50_to_law53_requires_decision_ref()
    {
        var (db, _, transitionService) = await CreateGovernanceContextAsync();
        var employee = await SeedEmployeeWithRegimeAsync(db, "law50");

        var error = await transitionService.ApplyTransitionAsync(
            employee.Id,
            "law53",
            "",
            "agency_reclassification",
            DateTime.UtcNow,
            appliedBy: null);

        Assert.Equal(ServiceRegimeTransitionRules.DecisionRefRequiredMessage, error);
    }

    [Fact]
    public async Task Transition_allowed_pair_succeeds()
    {
        var (db, _, transitionService) = await CreateGovernanceContextAsync();
        var employee = await SeedEmployeeWithRegimeAsync(db, "law50");
        var record = await db.EmploymentRecords.SingleAsync(r => r.EmployeeId == employee.Id);

        var error = await transitionService.ApplyTransitionAsync(
            employee.Id,
            "law53",
            "DEC-2026-001",
            "agency_reclassification",
            DateTime.UtcNow,
            appliedBy: null,
            record.Id);

        Assert.Null(error);

        var updated = await db.EmploymentRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id);
        Assert.Equal("law53", updated.LegalRegimeId);

        var history = await db.EmploymentRegimeTransitions.AsNoTracking()
            .Where(t => t.EmployeeId == employee.Id)
            .ToListAsync();
        Assert.Single(history);
        Assert.Equal("law50", history[0].FromRegimeId);
        Assert.Equal("law53", history[0].ToRegimeId);
        Assert.Equal("DEC-2026-001", history[0].TransitionDecisionRef);
    }

    [Fact]
    public void MapRegime_law53_to_LAW_53_2021()
    {
        Assert.Equal(ServiceRegimeToPayrollMapper.PayrollLaw53,
            ServiceRegimeToPayrollMapper.MapToPayrollCode("law53"));
        Assert.Equal(ServiceRegimeToPayrollMapper.PayrollLaw50,
            ServiceRegimeToPayrollMapper.MapToPayrollCode("law50"));
    }

    [Fact]
    public async Task PayrollGovernance_blocks_open_exception()
    {
        var employeeId = Guid.NewGuid();
        var client = new Mock<IHrCoreServiceRegimeClient>();
        client.Setup(c => c.GetPayrollBlockersAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HrCorePayrollBlockerDto(employeeId, true, ServiceRegimePayrollBlockService.OpenExceptionReason)]);

        var error = await PayrollRegimeGovernance.ValidateEmployeesAsync(
            PayrollRunType.Regular,
            [employeeId],
            client.Object,
            blocksFinalPayroll: true);

        Assert.NotNull(error);
        Assert.Contains("استثناء نظام خدمة", error);
    }

    private static async Task<Employee> SeedEmployeeWithRegimeAsync(HRCoreDbContext db, string regimeId)
    {
        var employee = new Employee
        {
            NationalId = Guid.NewGuid().ToString("N")[..10],
            FirstName = "Regime",
            LastName = "Test",
            FullName = "Regime Test",
            BirthDate = DateTime.UtcNow.AddYears(-30),
            Gender = HRManagement.SharedKernel.Enums.Gender.Male,
            HireDate = DateTime.UtcNow,
            CurrentStatus = HRManagement.SharedKernel.Enums.EmploymentStatus.Active
        };
        db.Employees.Add(employee);
        db.EmploymentRecords.Add(new EmploymentRecord
        {
            EmployeeId = employee.Id,
            PositionTitle = "موظف",
            StartDate = DateTime.UtcNow,
            EmploymentType = HRManagement.SharedKernel.Enums.EmploymentType.Permanent,
            Status = HRManagement.SharedKernel.Enums.EmploymentStatus.Active.ToString(),
            LegalRegimeId = regimeId
        });
        await db.SaveChangesAsync();
        return employee;
    }
}
