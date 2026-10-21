using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.InboundEvents;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;

namespace HRManagement.UnitTests;

/// <summary>SYR-008: movement regime binding tests.</summary>
public class Syr008MovementRegimeTests
{
    [Fact]
    public void MovementRules_secondment_preserves_regime()
    {
        Assert.True(ServiceRegimeMovementRules.PreservesRegime("secondment"));
        Assert.True(ServiceRegimeMovementRules.MayChangeRegime("transfer"));
    }

    [Fact]
    public async Task Transition_with_conflict_notes_persisted()
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

        var employee = new Employee
        {
            NationalId = "3333333333",
            FirstName = "Move",
            LastName = "Test",
            FullName = "Move Test",
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
            LegalRegimeId = "law50"
        });
        await db.SaveChangesAsync();

        var error = await transitionService.ApplyTransitionAsync(
            employee.Id,
            "law53",
            "DEC-TRANSFER-2026",
            "agency_reclassification",
            DateTime.UtcNow,
            appliedBy: null,
            conflictNotes: "Article 159 overlap noted");

        Assert.Null(error);
        var history = await db.EmploymentRegimeTransitions.SingleAsync();
        Assert.Equal("Article 159 overlap noted", history.ConflictNotes);
    }

    [Fact]
    public void Transfer_cross_regime_requires_decision_ref_on_request()
    {
        var transfer = new HR.Movements.Domain.Entities.TransferRequest
        {
            SourceLegalRegimeId = "law50",
            TargetLegalRegimeId = "law53"
        };

        var error = HR.Movements.Infrastructure.Services.MovementRegimeValidationService.ValidateTransferRegimeFields(transfer);
        Assert.Equal(ServiceRegimeMovementRules.CrossRegimeDecisionRequiredMessage, error);
    }

    [Fact]
    public async Task PayrollHandler_updates_RegimeCode_on_transfer()
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new PayrollDbContext(options);
        var employeeId = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = employeeId,
            BaseSalary = 1000,
            EffectiveFrom = DateTime.UtcNow.AddMonths(-1),
            IsActive = true,
            RegimeCode = ServiceRegimeToPayrollMapper.PayrollLaw50
        });
        await db.SaveChangesAsync();

        var repository = new PayrollRepository(db);
        var handler = new HandleTransferExecutedHandler(repository);
        await handler.Handle(new HandleTransferExecutedCommand(
            employeeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            null,
            null,
            ServiceRegimeToPayrollMapper.PayrollLaw53), CancellationToken.None);

        var profile = await db.EmployeePayProfiles.SingleAsync();
        Assert.Equal(ServiceRegimeToPayrollMapper.PayrollLaw53, profile.RegimeCode);
    }
}
