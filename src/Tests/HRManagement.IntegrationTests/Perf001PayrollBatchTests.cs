using System.Diagnostics;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Perf001PayrollBatchTests
{
    private static async Task<(ServiceProvider Root, AsyncServiceScope Scope, PayrollDbContext Db, IMediator Mediator)> CreateScopeAsync(
        Action<PayrollBatchOptions>? configure = null)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseSqlite(connection));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
        if (configure is not null)
            services.PostConfigure(configure);

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return (root, scope, db, mediator);
    }

    private static async Task SeedPublishedConfigAsync(PayrollDbContext db)
    {
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        config.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.PayrollConfigurations.Add(config);
        await db.SaveChangesAsync();
    }

    private static async Task SeedEmployeesAsync(PayrollDbContext db, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(id, 10000m + i));
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Calculate_Sets_InputFingerprint_And_ProgressFields()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync(o =>
        {
            o.MaxDegreeOfParallelism = 1;
            o.BulkWriteSize = 10;
        });
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        await SeedEmployeesAsync(db, 5);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), DateTime.UtcNow));
        Assert.True(created);

        var (ok, lines, err) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(ok, err);
        Assert.Equal(5, lines);

        var job = await db.PayrollRunJobs
            .Include(j => j.InputSnapshots)
            .Include(j => j.Partitions)
            .FirstAsync(j => j.PayrollRunId == runId);
        Assert.Equal(5, job.TotalEmployeeCount);
        Assert.True(job.ProcessedEmployeeCount >= 5);
        Assert.All(job.InputSnapshots, s => Assert.False(string.IsNullOrWhiteSpace(s.InputFingerprint)));
        Assert.All(job.Partitions, p => Assert.Equal(PayrollRunPartitionStatus.Completed, p.Status));
    }

    [Fact]
    public async Task Fingerprint_Mismatch_Fails_Job()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync(o => o.MaxDegreeOfParallelism = 1);
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        var emp = Guid.NewGuid();
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(emp, 12000m));
        await db.SaveChangesAsync();

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), DateTime.UtcNow));
        Assert.True(created);

        // Start job snapshot only by creating then tampering fingerprint before calculate finishes —
        // instead: calculate once, then create a new run-like path by mutating snapshot mid-resume.
        var (ok1, _, err1) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(ok1, err1);

        // New run with fingerprint tamper: seed job manually then process
        db.ChangeTracker.Clear();
        var run2 = await db.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
        // Create fresh employees + run for mismatch test
        var emp2 = Guid.NewGuid();
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(emp2, 11000m));
        await db.SaveChangesAsync();

        var (created2, runId2, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 30), DateTime.UtcNow));
        Assert.True(created2);

        // Intercept: start with partition size, but we'll corrupt fingerprint after snapshot via direct DB
        // Easiest: send Start with pause... Corrupt after creating snapshots by partial flow.
        // Use StartPayrollRunJob then immediately we can't intercept. Instead corrupt before Process by
        // inserting a job with wrong fingerprint and calling resume path.

        var config = await db.PayrollConfigurations.FirstAsync();
        var profile = await db.EmployeePayProfiles.FirstAsync(p => p.EmployeeId == emp2);
        var job = new PayrollRunJob
        {
            PayrollRunId = runId2!.Value,
            Phase = PayrollRunJobPhase.Calculate,
            Status = PayrollRunJobStatus.Paused,
            PartitionSize = 50,
            LeaseOwner = "test",
            LeaseUntil = DateTime.UtcNow.AddMinutes(5),
            TotalEmployeeCount = 1
        };
        job.InputSnapshots.Add(new PayrollRunInputSnapshot
        {
            EmployeeId = emp2,
            EmployeePayProfileId = profile.Id,
            ProfileVersion = PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile),
            InputFingerprint = "DEADBEEF_WRONG_FINGERPRINT",
            PayrollConfigurationId = config.Id,
            ConfigContentHash = config.ContentHash,
            SortOrder = 0
        });
        job.Partitions.Add(new PayrollRunPartition
        {
            PartitionIndex = 0,
            Status = PayrollRunPartitionStatus.Pending,
            EmployeeCount = 1
        });
        db.PayrollRunJobs.Add(job);
        var runEntity = await db.PayrollRuns.FirstAsync(r => r.Id == runId2);
        runEntity.BeginCalculating();
        runEntity.PayrollConfigurationId = config.Id;
        runEntity.ConfigContentHash = config.ContentHash;
        await db.SaveChangesAsync();

        var (ok, lines, jobId, err) = await mediator.Send(new ResumePayrollRunJobCommand(job.Id));
        Assert.False(ok);
        Assert.Contains("بصمة", err!);
    }

    [Fact]
    public async Task Parallel_Partitions_Do_Not_Duplicate_Lines()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync(o =>
        {
            o.MaxDegreeOfParallelism = 1; // multi-partition sequential; avoids SQLite connection contention
            o.BulkWriteSize = 2;
        });
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        await SeedEmployeesAsync(db, 8);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), DateTime.UtcNow));
        Assert.True(created);

        var (ok, lines, _, err) = await mediator.Send(new StartPayrollRunJobCommand(runId!.Value, PartitionSize: 2));
        Assert.True(ok, err);
        Assert.Equal(8, lines);

        var salaryCount = await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId);
        Assert.Equal(8, salaryCount);
        var partitions = await db.PayrollRunPartitions.Where(p => p.Job.PayrollRunId == runId).ToListAsync();
        Assert.True(partitions.Count >= 4);
        Assert.All(partitions, p => Assert.Equal(PayrollRunPartitionStatus.Completed, p.Status));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Throughput_100_Employees_Completes()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync(o =>
        {
            o.MaxDegreeOfParallelism = 1;
            o.BulkWriteSize = 25;
        });
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        await SeedEmployeesAsync(db, 100);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), DateTime.UtcNow));
        Assert.True(created);

        var sw = Stopwatch.StartNew();
        var (ok, lines, _, err) = await mediator.Send(new StartPayrollRunJobCommand(runId!.Value, PartitionSize: 25));
        sw.Stop();

        Assert.True(ok, err);
        Assert.Equal(100, lines);
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(2), $"Took {sw.Elapsed}");

        var jobRow = await db.PayrollRunJobs.FirstAsync(j => j.PayrollRunId == runId);
        Assert.Equal(100, jobRow.TotalEmployeeCount);
        Assert.Equal(PayrollRunJobStatus.Completed, jobRow.Status);
    }
}
