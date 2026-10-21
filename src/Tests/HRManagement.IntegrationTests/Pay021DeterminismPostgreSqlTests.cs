using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Services;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-06 AC-21: dual run bit-for-bit determinism on PostgreSQL.</summary>
[Collection("PostgreSql")]
public class Pay021DeterminismPostgreSqlTests
{
    private readonly PostgreSqlFixture _fixture;

    public Pay021DeterminismPostgreSqlTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task DualRun_SameFrozenInputs_BitForBit_On_PostgreSQL()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");

        var (root, scope, db, mediator) = await PayrollPostgresTestSupport.CreateScopeAsync(_fixture);
        await using var _ = scope;
        await using var __ = root;

        await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
        await PayrollPostgresTestSupport.SeedEmployeesAsync(db, 12);

        var periodStart = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);

        var (createdA, runAId, errA) = await mediator.Send(new CreatePayrollRunCommand(periodStart, periodEnd, DateTime.UtcNow));
        Assert.True(createdA, errA);

        var (okA, linesA, calcErrA) = await mediator.Send(new CalculatePayrollRunCommand(runAId!.Value));
        Assert.True(okA, calcErrA);
        Assert.Equal(12, linesA);

        var configHashA = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Id == runAId)
            .Select(r => r.ConfigContentHash)
            .FirstAsync();

        await PayrollPostgresTestSupport.CancelRunForDualTestAsync(db, runAId!.Value);

        var (createdB, runBId, errB) = await mediator.Send(new CreatePayrollRunCommand(periodStart, periodEnd, DateTime.UtcNow));
        Assert.True(createdB, errB);

        var (okB, linesB, calcErrB) = await mediator.Send(new CalculatePayrollRunCommand(runBId!.Value));
        Assert.True(okB, calcErrB);
        Assert.Equal(12, linesB);

        var linesAEntities = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == runAId).ToListAsync();
        var linesBEntities = await db.SalaryLines.AsNoTracking().Where(l => l.PayrollRunId == runBId).ToListAsync();
        var lineIdsA = linesAEntities.Select(l => l.Id).ToList();
        var lineIdsB = linesBEntities.Select(l => l.Id).ToList();
        var compsA = await db.SalaryLineComponents.AsNoTracking().Where(c => lineIdsA.Contains(c.SalaryLineId)).ToListAsync();
        var compsB = await db.SalaryLineComponents.AsNoTracking().Where(c => lineIdsB.Contains(c.SalaryLineId)).ToListAsync();

        var canonicalA = PayrollDeterminismComparer.ExportCanonical(linesAEntities, compsA);
        var canonicalB = PayrollDeterminismComparer.ExportCanonical(linesBEntities, compsB);

        Assert.Null(PayrollDeterminismComparer.BuildDiff(canonicalA, canonicalB));
        Assert.True(PayrollDeterminismComparer.AreBitForBitEqual(canonicalA, canonicalB));

        var configHashB = await db.PayrollRuns.AsNoTracking()
            .Where(r => r.Id == runBId)
            .Select(r => r.ConfigContentHash)
            .FirstAsync();
        Assert.Equal(configHashA, configHashB);
        Assert.False(string.IsNullOrEmpty(configHashA));
    }
}
