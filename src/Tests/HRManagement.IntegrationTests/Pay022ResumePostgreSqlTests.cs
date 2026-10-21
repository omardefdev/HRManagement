using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-06 AC-22: pause/resume idempotency and lease conflict on PostgreSQL.</summary>
[Collection("PostgreSql")]
public class Pay022ResumePostgreSqlTests
{
    private readonly PostgreSqlFixture _fixture;

    public Pay022ResumePostgreSqlTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Pause_Resume_CompletesWithoutDuplicates_On_PostgreSQL()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");

        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(
            _fixture,
            o =>
            {
                o.MaxDegreeOfParallelism = 2;
                o.DefaultPartitionSize = 5;
            });
        await using var _ = scope;
        await using var __ = root;

        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 12);

        var (created, runId, err) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow));
        Assert.True(created, err);

        var start = await mediator.Send(new StartPayrollRunJobCommand(runId!.Value, PartitionSize: 5));
        Assert.True(start.Success, start.Error);
        Assert.Equal(12, start.LinesGenerated);

        db.ChangeTracker.Clear();
        var job = await db.PayrollRunJobs.Include(j => j.Partitions).FirstAsync(j => j.PayrollRunId == runId);
        job.Status = PayrollRunJobStatus.Paused;
        job.ResultsValidated = false;
        var lastPartition = job.Partitions.OrderBy(p => p.PartitionIndex).Last();
        lastPartition.Status = PayrollRunPartitionStatus.Pending;
        lastPartition.ProcessedCount = 0;

        var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.ResultsFrozen = false;
        run.ValidatedAtCalculate = false;
        run.Status = PayrollRunStatus.Calculating;

        var lastEmp = await db.PayrollRunInputSnapshots
            .Where(s => s.PayrollRunJobId == job.Id)
            .OrderBy(s => s.SortOrder)
            .Select(s => s.EmployeeId)
            .LastAsync();
        var line = await db.SalaryLines.FirstAsync(l => l.PayrollRunId == runId && l.EmployeeId == lastEmp);
        var comps = await db.SalaryLineComponents.Where(c => c.SalaryLineId == line.Id).ToListAsync();
        db.SalaryLineComponents.RemoveRange(comps);
        db.SalaryLines.Remove(line);
        await db.SaveChangesAsync();

        var beforeCount = await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId);
        Assert.True(beforeCount >= 1);

        var resume = await mediator.Send(new ResumePayrollRunJobCommand(job.Id));
        Assert.True(resume.Success, resume.Error);
        Assert.True(resume.LinesGenerated >= 1);

        db.ChangeTracker.Clear();
        var finalLines = await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId);
        Assert.Equal(12, finalLines);
        Assert.Equal(12, await db.PayrollRunJobs.Where(j => j.PayrollRunId == runId)
            .Select(j => j.ProcessedEmployeeCount).FirstAsync());
        Assert.All(
            await db.PayrollRunPartitions.Where(p => p.PayrollRunJobId == job.Id).ToListAsync(),
            p => Assert.Equal(PayrollRunPartitionStatus.Completed, p.Status));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task DoubleStart_WhileLeaseActive_ReturnsConflict_On_PostgreSQL()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");

        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(_fixture);
        await using var _ = scope;
        await using var __ = root;

        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 2);

        var (created, runId, err) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow));
        Assert.True(created, err);

        var job = new Payroll.Domain.Entities.PayrollRunJob
        {
            PayrollRunId = runId!.Value,
            Status = PayrollRunJobStatus.Running,
            Phase = PayrollRunJobPhase.Calculate,
            LeaseUntil = DateTime.UtcNow.AddMinutes(10),
            LeaseOwner = "pg-test"
        };
        db.PayrollRunJobs.Add(job);
        var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.Status = PayrollRunStatus.Calculating;
        await db.SaveChangesAsync();

        var (ok, _, calcErr) = await mediator.Send(new CalculatePayrollRunCommand(runId.Value));
        Assert.False(ok);
        Assert.Contains("نشطة", calcErr);
    }
}
