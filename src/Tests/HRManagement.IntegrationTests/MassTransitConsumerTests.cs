using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Domain.Entities;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class MassTransitConsumerTests
{
    private static async Task<MassTransitTestScope> CreatePayrollScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
                connection.Open();
                connections.Add(connection);
                IntegrationTestHelpers.AddSqliteDbContext<PayrollDbContext>(services, connection);
                PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
            },
            cfg =>
            {
                cfg.AddConsumer<LeaveApprovedConsumer>();
                cfg.AddConsumer<SeparationPartyConsumer>();
                cfg.AddConsumer<EmployeeCreatedConsumer>();
                cfg.AddConsumer<SecondmentExecutedConsumer>();
                cfg.AddConsumer<MissionApprovedConsumer>();
                cfg.AddConsumer<DisciplinaryActionTakenConsumer>();
            });

        await scope.GetDb<PayrollDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    [Fact]
    public async Task LeaveApproved_UnpaidLeave_Creates_ExactlyOneDeductionHolder()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var db = scope.GetDb<PayrollDbContext>();
        var empId = Guid.NewGuid();
        var leaveId = Guid.NewGuid();
        var start = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = empId,
            BaseSalary = 30000m,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ILeaveApproved>(new
        {
            TransactionId = Guid.NewGuid(),
            LeaveRequestId = leaveId,
            EmployeeId = empId,
            ApprovedBy = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            LeaveType = "Unpaid",
            StartDate = start,
            EndDate = start.AddDays(4),
            DurationDays = 5
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ILeaveApproved>(scope.Harness);
        db.ChangeTracker.Clear();

        // Unpaid leave used to produce both a DeductionRecord and an AbsenceDeduction of the same
        // amount, and the engine charged both — the employee paid twice for one leave.
        var deductions = await db.DeductionRecords.Where(d => d.EmployeeId == empId).ToListAsync();
        Assert.Empty(deductions);

        var absences = await db.AbsenceDeductions.Where(a => a.EmployeeId == empId).ToListAsync();
        var absence = Assert.Single(absences);
        Assert.True(absence.Amount > 0);
        Assert.Equal(5, absence.AbsentDays);
        Assert.Equal($"LeaveRequestId={leaveId}", absence.SourceReference);

        // Period comes from the leave, not from the approval date, so the charge lands in the
        // payroll period the employee was actually absent in.
        Assert.Equal(DateOnly.FromDateTime(start), absence.PeriodStart);
        Assert.Equal(DateOnly.FromDateTime(start.AddDays(4)), absence.PeriodEnd);
    }

    [Fact]
    public async Task LeaveApproved_PaidLeave_NoDeduction()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var empId = Guid.NewGuid();

        await scope.Harness.Bus.Publish<ILeaveApproved>(new
        {
            TransactionId = Guid.NewGuid(),
            LeaveRequestId = Guid.NewGuid(),
            EmployeeId = empId,
            ApprovedBy = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            LeaveType = "Annual",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(10),
            DurationDays = 10
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ILeaveApproved>(scope.Harness);
        var db = scope.GetDb<PayrollDbContext>();
        db.ChangeTracker.Clear();
        var deductions = await db.DeductionRecords
            .Where(d => d.EmployeeId == empId && d.DeductionType == "UnpaidLeave")
            .ToListAsync();
        Assert.Empty(deductions);
    }

    [Fact]
    public async Task SeparationCompleted_DeactivatesProfile_CreatesSettlement()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var db = scope.GetDb<PayrollDbContext>();
        var empId = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = empId,
            BaseSalary = 50000m,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddYears(-3)
        });
        await db.SaveChangesAsync();

        var sepId = Guid.NewGuid();
        await scope.Harness.Bus.Publish<IExecuteSeparationParty>(new
        {
            SeparationRecordId = sepId,
            EmployeeId = empId,
            Party = ProcessParties.Payroll,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = (string?)null,
            TenantId = (Guid?)null,
            Compensate = false,
            RemainingLeaveDays = 0
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IExecuteSeparationParty>(scope.Harness);
        db.ChangeTracker.Clear();
        var profile = await db.EmployeePayProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.EmployeeId == empId);
        Assert.NotNull(profile);
        Assert.False(profile.IsActive);

        var settlements = await db.EndOfServiceSettlements.Where(e => e.EmployeeId == empId).ToListAsync();
        Assert.Single(settlements);
    }

    [Fact]
    public async Task EmployeeCreated_CreatesPayProfile()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var empId = Guid.NewGuid();

        await scope.Harness.Bus.Publish<IEmployeeCreated>(new
        {
            EmployeeId = empId,
            FullName = "Test Employee",
            NationalId = "1234567890",
            EmploymentType = "FullTime",
            TenantId = (Guid?)null
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IEmployeeCreated>(scope.Harness);
        var db = scope.GetDb<PayrollDbContext>();
        db.ChangeTracker.Clear();
        var profile = await db.EmployeePayProfiles.FirstOrDefaultAsync(p => p.EmployeeId == empId);
        Assert.NotNull(profile);
    }

    [Fact]
    public async Task LeaveApproved_WithExistingMarker_DoesNotDuplicateAdjustment()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var db = scope.GetDb<PayrollDbContext>();
        var empId = Guid.NewGuid();
        var leaveId = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = empId,
            BaseSalary = 30000m,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        });
        db.PayrollAdjustments.Add(new PayrollAdjustment
        {
            EmployeeId = empId,
            AdjustmentReason = $"existing — LeaveRequestId={leaveId}"
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ILeaveApproved>(new
        {
            TransactionId = Guid.NewGuid(),
            LeaveRequestId = leaveId,
            EmployeeId = empId,
            ApprovedBy = Guid.NewGuid(),
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            LeaveType = "Unpaid",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(5),
            DurationDays = 5
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ILeaveApproved>(scope.Harness);
        db.ChangeTracker.Clear();
        var adjustments = await db.PayrollAdjustments.Where(a => a.EmployeeId == empId).ToListAsync();
        Assert.Equal(2, adjustments.Count);
    }

    [Fact]
    public async Task SecondmentExecuted_Creates_PayrollAdjustment()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var empId = Guid.NewGuid();
        var secondmentId = Guid.NewGuid();

        await scope.Harness.Bus.Publish<ISecondmentExecuted>(new
        {
            SecondmentRequestId = secondmentId,
            EmployeeId = empId,
            HostOrg = "Host Unit",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddMonths(3),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISecondmentExecuted>(scope.Harness);
        var db = scope.GetDb<PayrollDbContext>();
        db.ChangeTracker.Clear();
        var adjustments = await db.PayrollAdjustments
            .Where(a => a.EmployeeId == empId && a.AdjustmentReason!.Contains($"Secondment-{secondmentId}"))
            .ToListAsync();
        Assert.Single(adjustments);
    }

    [Fact]
    public async Task MissionApproved_Creates_PayrollAdjustment()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var empId = Guid.NewGuid();
        var missionId = Guid.NewGuid();

        await scope.Harness.Bus.Publish<IMissionApproved>(new
        {
            MissionFileId = missionId,
            EmployeeId = empId,
            Destination = "UK",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddYears(1),
            CorrelationId = Guid.NewGuid(),
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IMissionApproved>(scope.Harness);
        var db = scope.GetDb<PayrollDbContext>();
        db.ChangeTracker.Clear();
        var adjustments = await db.PayrollAdjustments
            .Where(a => a.EmployeeId == empId && a.AdjustmentReason!.Contains($"Mission-{missionId}"))
            .ToListAsync();
        Assert.Single(adjustments);
    }

    [Fact]
    public async Task DisciplinaryActionTaken_Suspension_Creates_Deduction()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var db = scope.GetDb<PayrollDbContext>();
        var empId = Guid.NewGuid();
        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = empId,
            BaseSalary = 30000m,
            IsActive = true,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1)
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<IDisciplinaryActionTaken>(new
        {
            EmployeeId = empId,
            ActionType = "Suspension",
            IssuedAt = DateTime.UtcNow,
            Notes = "test",
            ComplaintId = (Guid?)null,
            CorrelationId = (Guid?)null,
            ActionId = Guid.NewGuid(),
            SourceSystem = "Test",
            PenaltyDays = 3
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IDisciplinaryActionTaken>(scope.Harness);
        db.ChangeTracker.Clear();
        var deductions = await db.DeductionRecords.Where(d => d.EmployeeId == empId).ToListAsync();
        Assert.Single(deductions);
        Assert.Equal("DisciplinaryDeduction", deductions[0].DeductionType);
    }
}
