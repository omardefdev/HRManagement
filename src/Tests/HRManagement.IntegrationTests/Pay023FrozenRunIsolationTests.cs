using Microsoft.EntityFrameworkCore;
using HRManagement.TestSupport.Time;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-10 AC-23: a frozen run is isolated from a pay change. PLAN-53 (P-11) keeps that meaning and proves it twice, with the clock in
/// the frozen month: (1) the manual path cannot reach the frozen month at all (the approval is refused and the request stays pending);
/// (2) an automatic change, which a frozen month does not refuse, is applied and still leaves the frozen run's lines as they were (the
/// run reads its own snapshot), while a later run reflects it. The direct write this test used to send is gone.
/// </summary>
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

        // Mid-May 2026: May is the current month, so a manual request may name it.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 5, 15, 9, 0, 0, TimeSpan.Zero));
        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(
            _fixture, configureServices: services => services.AddSingleton<TimeProvider>(clock));
        await using var _ = scope;
        await using var __ = root;

        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 3);

        var employeeId = Guid.Parse("b0000001-0000-4000-8000-000000000001");
        var original = await db.EmployeePayProfiles.AsNoTracking().FirstAsync(p => p.EmployeeId == employeeId && p.IsActive);
        var originalSalary = original.BaseSalary;

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
        var processor = Guid.NewGuid();
        var approver = Guid.NewGuid();

        // (1) The manual path: a request for the frozen month is accepted as a request, and its approval is refused.
        db.ChangeTracker.Clear();
        var submitted = await mediator.Send(new SubmitPayProfileChangeCommand(
            employeeId, raisedSalary, null, new DateOnly(2026, 5, 1), "AC-23: زيادة بعد التجميد", "القرار 23 لعام 2026",
            PayProfileChangeSource.Manual, null, processor, "processor"));
        Assert.True(submitted.Success, submitted.Error);
        Assert.Equal(PayProfileChangeState.PendingApproval, submitted.State);

        db.ChangeTracker.Clear();
        var approval = await mediator.Send(new DecidePayProfileChangeCommand(
            submitted.RequestId!.Value, PayProfileChangeDecision.Approved, null, approver, "approver"));
        Assert.False(approval.Success);
        Assert.Equal(PayProfileChangeFailure.Conflict, approval.Failure);
        Assert.Contains("2026/05", approval.Error ?? "", StringComparison.Ordinal);

        db.ChangeTracker.Clear();
        Assert.Equal(PayProfileChangeState.PendingApproval,
            (await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.Id == submitted.RequestId)).State);
        var stillActive = await db.EmployeePayProfiles.AsNoTracking().SingleAsync(p => p.EmployeeId == employeeId && p.IsActive);
        Assert.Equal(original.Id, stillActive.Id);
        Assert.Equal(originalSalary, stillActive.BaseSalary);
        Assert.False(await db.EmployeePayProfileHistories.AsNoTracking().AnyAsync(h => h.EmployeeId == employeeId));

        // (2) An automatic change (a promotion decision) is not refused by a frozen month: it is applied from June.
        var applier = scope.ServiceProvider.GetRequiredService<IPayProfileChangeApplier>();
        var system = await applier.ApplySystemChangeAsync(
            employeeId, PayProfileChangeKind.BaseSalary, PayProfileChangeSource.Promotion, "قرار ترفيع 14/2026",
            raisedSalary, null, new DateOnly(2026, 6, 1));
        Assert.True(system.Success, system.Error);
        Assert.Equal(PayProfileChangeState.Applied, system.State);

        db.ChangeTracker.Clear();
        Assert.Equal(raisedSalary, (await db.EmployeePayProfiles.AsNoTracking().SingleAsync(p => p.EmployeeId == employeeId && p.IsActive)).BaseSalary);
        // The pending manual request was written against the version the promotion replaced: it is superseded, not left open.
        var superseded = await db.PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.Id == submitted.RequestId);
        Assert.Equal(PayProfileChangeState.Superseded, superseded.State);
        Assert.Equal(system.RequestId, superseded.SupersededByRequestId);

        // The frozen run is isolated exactly as before: no recalculation, and its line is unchanged.
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
