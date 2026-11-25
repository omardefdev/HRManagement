using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (G-PAY-06): the validation checks the run's state before anything else. It used to check none and then set «محسوب» from
/// any state, so a call to <c>validate</c> brought back an approved, posted or reversed run. Here it is sent to a run in every state.
/// </summary>
[Collection("IntegrationSerial")]
public class Pay056GuardTests
{
    private static async Task<Guid> PlantAsync(Pay056RunScope scope, PayrollRunStatus status, bool frozen, bool validated)
    {
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2028, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            RunDate = new DateTime(2028, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = status,
            TotalNetPay = 900m,
            ResultsFrozen = frozen,
            ValidatedAtCalculate = validated,
        };
        var line = new SalaryLine { PayrollRunId = run.Id, EmployeeId = Guid.NewGuid(), GrossPay = 1000m, TotalDeductions = 100m, NetPay = 900m };
        run.SalaryLines.Add(line);
        scope.Db.PayrollRuns.Add(run);
        scope.Db.SalaryLineComponents.AddRange(TestDataFactory.ComponentsOf(line));
        await scope.Db.SaveChangesAsync();
        scope.Db.ChangeTracker.Clear();
        return run.Id;
    }

    [Theory]
    [InlineData(PayrollRunStatus.Calculating)]
    [InlineData(PayrollRunStatus.Validating)]
    [InlineData(PayrollRunStatus.Calculated)]
    public async Task Validate_works_on_a_run_that_is_being_calculated_or_was_calculated(PayrollRunStatus status)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status, frozen: false, validated: false);

        var (ok, error) = await scope.Mediator.Send(new ValidatePayrollRunCommand(runId));

        Assert.True(ok, error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Calculated, run.Status);
        Assert.True(run.ResultsFrozen);
        Assert.True(run.ValidatedAtCalculate);
    }

    [Fact]
    public async Task Validate_again_on_a_validated_run_changes_nothing()
    {
        // The six callers that send validate right after a calculation (AC-23, AC-24, AC-25, AC-IAM-53, Pay023) rely on this.
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, PayrollRunStatus.Calculated, frozen: true, validated: true);

        var (ok, error) = await scope.Mediator.Send(new ValidatePayrollRunCommand(runId));

        Assert.True(ok, error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Calculated, run.Status);
        Assert.Equal(900m, run.TotalNetPay);
    }

    [Theory]
    [InlineData(PayrollRunStatus.Draft, false, false)]
    [InlineData(PayrollRunStatus.UnderReview, true, true)]
    [InlineData(PayrollRunStatus.Approved, true, true)]
    [InlineData(PayrollRunStatus.Posted, true, true)]
    [InlineData(PayrollRunStatus.Reversed, true, true)]
    [InlineData(PayrollRunStatus.Cancelled, false, false)]
    public async Task Validate_is_refused_in_every_other_state_and_the_run_stays_as_it_was(PayrollRunStatus status, bool frozen, bool validated)
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var runId = await PlantAsync(scope, status, frozen, validated);
        // The total is made wrong on purpose: a validation that got as far as the totals would answer about them, not about the state.
        await scope.Db.Database.ExecuteSqlRawAsync("""UPDATE "PayrollRuns" SET "TotalNetPay" = 1 WHERE "Id" = {0}""", runId);

        var (ok, error) = await scope.Mediator.Send(new ValidatePayrollRunCommand(runId));

        Assert.False(ok);
        Assert.Contains("لا يمكن التحقق من تشغيل", error);
        var run = await scope.RunAsync(runId);
        Assert.Equal(status, run.Status);
        Assert.Equal(frozen, run.ResultsFrozen);
        Assert.Equal(validated, run.ValidatedAtCalculate);
        Assert.Equal(1m, run.TotalNetPay);
    }

    [Fact]
    public async Task A_calculation_that_fails_its_validation_leaves_the_run_being_calculated_by_its_own_method()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var employee = await scope.EmployeeAsync();
        var runId = await scope.CreateRunAsync(2028, 4);

        // A line of another employee is planted before the calculation: the snapshot has one employee, the run will have two lines,
        // and the calculator's validation fails on the count.
        scope.Db.SalaryLines.Add(new SalaryLine { PayrollRunId = runId, EmployeeId = Guid.NewGuid(), GrossPay = 10m, NetPay = 10m });
        await scope.Db.SaveChangesAsync();

        var (ok, _, error) = await scope.CalculateAsync(runId);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        var run = await scope.RunAsync(runId);
        Assert.Equal(PayrollRunStatus.Calculating, run.Status);
        Assert.False(run.ResultsFrozen);
        Assert.False(run.ValidatedAtCalculate);
        Assert.Equal(PayrollRunJobStatus.Failed, (await scope.Db.PayrollRunJobs.AsNoTracking().SingleAsync(j => j.PayrollRunId == runId)).Status);
        Assert.NotEqual(Guid.Empty, employee);
    }
}
