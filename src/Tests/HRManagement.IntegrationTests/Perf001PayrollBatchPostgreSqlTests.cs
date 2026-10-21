using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-06: PERF-001 parallel batch on PostgreSQL — no duplicate salary lines.</summary>
[Collection("PostgreSql")]
public class Perf001PayrollBatchPostgreSqlTests
{
    private readonly PostgreSqlFixture _fixture;

    public Perf001PayrollBatchPostgreSqlTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task ParallelPartitions_DoNotDuplicateLines_On_PostgreSQL()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");

        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(
            _fixture,
            o =>
            {
                o.MaxDegreeOfParallelism = 4;
                o.BulkWriteSize = 5;
                o.DefaultPartitionSize = 10;
            });
        await using var _ = scope;
        await using var __ = root;

        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 24);

        var (created, runId, err) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2027, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 2, 28, 0, 0, 0, DateTimeKind.Utc),
            DateTime.UtcNow));
        Assert.True(created, err);

        var (ok, lines, _, calcErr) = await mediator.Send(new StartPayrollRunJobCommand(runId!.Value, PartitionSize: 6));
        Assert.True(ok, calcErr);
        Assert.Equal(24, lines);

        var lineCount = await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId);
        Assert.Equal(24, lineCount);

        var duplicateEmployees = await db.SalaryLines
            .Where(l => l.PayrollRunId == runId)
            .GroupBy(l => l.EmployeeId)
            .Where(g => g.Count() > 1)
            .CountAsync();
        Assert.Equal(0, duplicateEmployees);
    }
}
