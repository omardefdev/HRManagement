using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.EmployeePayProfiles;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-10 AC-23: frozen run isolation after pay profile change.</summary>
[Collection("PostgreSql")]
public class Pay023FrozenRunIsolationTests
{
    private readonly PostgreSqlFixture _fixture;

    public Pay023FrozenRunIsolationTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Frozen_run_unchanged_after_profile_edit_later_run_reflects_change()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");

        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(_fixture);
        await using var _ = scope;
        await using var __ = root;

        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 3);

        var employeeId = Guid.Parse("b0000001-0000-4000-8000-000000000001");
        var originalSalary = await db.EmployeePayProfiles
            .AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .Select(p => p.BaseSalary)
            .FirstAsync();

        var periodStartA = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndA = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);

        var (createdA, runAId, errA) = await mediator.Send(new CreatePayrollRunCommand(
            periodStartA, periodEndA, DateTime.UtcNow));
        Assert.True(createdA, errA);

        var (calcOkA, _, calcErrA) = await mediator.Send(new CalculatePayrollRunCommand(runAId!.Value));
        Assert.True(calcOkA, calcErrA);

        var (validateOkA, validateErrA) = await mediator.Send(new ValidatePayrollRunCommand(runAId.Value));
        Assert.True(validateOkA, validateErrA);

        var runA = await db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runAId);
        Assert.True(runA.ResultsFrozen);
        Assert.True(runA.ValidatedAtCalculate);

        var lineBefore = await db.SalaryLines.AsNoTracking()
            .FirstAsync(l => l.PayrollRunId == runAId && l.EmployeeId == employeeId);
        var netBefore = lineBefore.NetPay;

        var raisedSalary = originalSalary + 50_000m;
        var (profileOk, _, profileErr) = await mediator.Send(new UpsertEmployeePayProfileCommand(
            employeeId,
            raisedSalary,
            null,
            DateTime.UtcNow,
            null,
            true,
            "AC-23 raise after freeze"));
        Assert.True(profileOk, profileErr);

        var (recalcOk, _, recalcErr) = await mediator.Send(new CalculatePayrollRunCommand(runAId.Value));
        Assert.False(recalcOk);
        Assert.Contains("تجميد", recalcErr ?? "", StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        var lineAfterEdit = await db.SalaryLines.AsNoTracking()
            .FirstAsync(l => l.PayrollRunId == runAId && l.EmployeeId == employeeId);
        Assert.Equal(netBefore, lineAfterEdit.NetPay);

        var periodStartB = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEndB = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var (createdB, runBId, errB) = await mediator.Send(new CreatePayrollRunCommand(
            periodStartB, periodEndB, DateTime.UtcNow));
        Assert.True(createdB, errB);

        var (calcOkB, _, calcErrB) = await mediator.Send(new CalculatePayrollRunCommand(runBId!.Value));
        Assert.True(calcOkB, calcErrB);

        var lineB = await db.SalaryLines.AsNoTracking()
            .FirstAsync(l => l.PayrollRunId == runBId && l.EmployeeId == employeeId);
        Assert.NotEqual(netBefore, lineB.NetPay);
        Assert.True(lineB.NetPay > netBefore);
    }
}
