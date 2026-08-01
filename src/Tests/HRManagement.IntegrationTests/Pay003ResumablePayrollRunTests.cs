using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Pay003ResumablePayrollRunTests
{
    private static async Task<(ServiceProvider Root, AsyncServiceScope Scope, PayrollDbContext Db, IMediator Mediator)> CreateScopeAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseSqlite(connection));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);

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

    private static async Task SeedEmployeesAsync(PayrollDbContext db, params Guid[] employeeIds)
    {
        foreach (var id in employeeIds)
            db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(id, 10000m + (id.GetHashCode() % 1000)));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Calculate_PerEmployee_Atomic_NoOrphanComponents()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        var emp = Guid.NewGuid();
        await SeedEmployeesAsync(db, emp);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 5, 1), new DateTime(2026, 5, 31), DateTime.UtcNow));
        Assert.True(created);

        var (ok, lines, err) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(ok, err);
        Assert.Equal(1, lines);

        var salaryLines = await db.SalaryLines.Where(l => l.PayrollRunId == runId).ToListAsync();
        Assert.Single(salaryLines);
        var comps = await db.SalaryLineComponents.Where(c => c.SalaryLineId == salaryLines[0].Id).ToListAsync();
        Assert.NotEmpty(comps);
        Assert.True(await db.PayrollRuns.AnyAsync(r => r.Id == runId && r.ResultsFrozen && r.ValidatedAtCalculate));
    }

    [Fact]
    public async Task Calculate_Restart_SkipsCompletedEmployees()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        await SeedEmployeesAsync(db, e1, e2);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 6, 1), new DateTime(2026, 6, 30), DateTime.UtcNow));
        Assert.True(created);

        var (ok1, lines1, err1) = await mediator.Send(new CalculatePayrollRunCommand(runId!.Value));
        Assert.True(ok1, err1);
        Assert.Equal(2, lines1);

        // Unfreeze to allow restart path (simulate kill mid-run by clearing freeze + job completed → new calc blocked)
        db.ChangeTracker.Clear();
        var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.ResultsFrozen = false;
        run.ValidatedAtCalculate = false;
        run.Status = PayrollRunStatus.Calculating;
        var job = await db.PayrollRunJobs.FirstAsync(j => j.PayrollRunId == runId);
        job.Status = PayrollRunJobStatus.Paused;
        await db.SaveChangesAsync();

        var before = await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId);
        var (ok2, lines2, err2) = await mediator.Send(new CalculatePayrollRunCommand(runId.Value));
        Assert.True(ok2, err2);
        Assert.Equal(0, lines2); // both skipped idempotently
        var after = await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId);
        Assert.Equal(before, after);
        Assert.Equal(2, after);
    }

    [Fact]
    public async Task Job_DoubleStart_RejectedOrResumes()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        await SeedEmployeesAsync(db, Guid.NewGuid());

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 31), DateTime.UtcNow));
        Assert.True(created);

        // Seed an active Running job with future lease
        var job = new PayrollRunJob
        {
            PayrollRunId = runId!.Value,
            Status = PayrollRunJobStatus.Running,
            Phase = PayrollRunJobPhase.Calculate,
            LeaseUntil = DateTime.UtcNow.AddMinutes(10),
            LeaseOwner = "test"
        };
        db.PayrollRunJobs.Add(job);
        var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.Status = PayrollRunStatus.Calculating;
        await db.SaveChangesAsync();

        var (ok, _, err) = await mediator.Send(new CalculatePayrollRunCommand(runId.Value));
        Assert.False(ok);
        Assert.Contains("نشطة", err);
        Assert.Equal(1, await db.PayrollRunJobs.CountAsync(j => j.PayrollRunId == runId));
    }

    [Fact]
    public async Task Pause_Resume_ContinuesFromCheckpoint()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        await SeedEmployeesAsync(db, e1, e2);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), DateTime.UtcNow));
        Assert.True(created);

        // Create job+snapshot manually with one completed partition employee already saved
        var start = await mediator.Send(new StartPayrollRunJobCommand(runId!.Value, PartitionSize: 1));
        Assert.True(start.Success, start.Error);
        Assert.Equal(2, start.LinesGenerated);

        db.ChangeTracker.Clear();
        var job = await db.PayrollRunJobs.Include(j => j.Partitions).FirstAsync(j => j.PayrollRunId == runId);
        Assert.Equal(PayrollRunJobStatus.Completed, job.Status);
        Assert.All(job.Partitions, p => Assert.Equal(PayrollRunPartitionStatus.Completed, p.Status));

        // Simulate pause mid-partition then resume: set job Paused with incomplete partition
        job.Status = PayrollRunJobStatus.Paused;
        job.ResultsValidated = false;
        var last = job.Partitions.OrderBy(p => p.PartitionIndex).Last();
        last.Status = PayrollRunPartitionStatus.Pending;
        last.ProcessedCount = 0;
        var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.ResultsFrozen = false;
        run.ValidatedAtCalculate = false;
        run.Status = PayrollRunStatus.Calculating;
        // Remove last employee's line to force recompute on resume
        var lastSnapEmp = await db.PayrollRunInputSnapshots
            .Where(s => s.PayrollRunJobId == job.Id)
            .OrderBy(s => s.SortOrder)
            .Select(s => s.EmployeeId)
            .LastAsync();
        var line = await db.SalaryLines.FirstAsync(l => l.PayrollRunId == runId && l.EmployeeId == lastSnapEmp);
        var comps = await db.SalaryLineComponents.Where(c => c.SalaryLineId == line.Id).ToListAsync();
        db.SalaryLineComponents.RemoveRange(comps);
        db.SalaryLines.Remove(line);
        await db.SaveChangesAsync();

        var resume = await mediator.Send(new ResumePayrollRunJobCommand(job.Id));
        Assert.True(resume.Success, resume.Error);
        Assert.Equal(1, resume.LinesGenerated);
        Assert.Equal(2, await db.SalaryLines.CountAsync(l => l.PayrollRunId == runId));
    }

    [Fact]
    public async Task Validate_Fails_OnMismatchedTotals()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Calculated,
            TotalNetPay = 99999m
        };
        var line = new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            NetPay = 900m
        };
        run.SalaryLines.Add(line);
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync();
        db.SalaryLineComponents.Add(new SalaryLineComponent
        {
            SalaryLineId = line.Id,
            ComponentType = SalaryComponentType.BaseSalary,
            ComponentName = "أساسي",
            Amount = 900m
        });
        await db.SaveChangesAsync();

        var (ok, err) = await mediator.Send(new ValidatePayrollRunCommand(run.Id));
        Assert.False(ok);
        Assert.Contains("TotalNetPay", err!);
    }

    [Fact]
    public async Task PartialRun_CannotPost()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 5, 1),
            PeriodEnd = new DateTime(2026, 5, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Approved,
            TotalNetPay = 900m,
            ValidatedAtCalculate = false,
            ResultsFrozen = false
        };
        run.SalaryLines.Add(new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            NetPay = 900m
        });
        run.Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = run.Id,
            ApproverId = Guid.NewGuid(),
            Decision = ApprovalDecision.Approved,
            DecidedAt = DateTime.UtcNow
        });
        db.PayrollRuns.Add(run);
        db.PayrollAuditRecords.Add(new PayrollAuditRecord
        {
            PayrollRunId = run.Id,
            ClosedBy = Guid.NewGuid(),
            ClosedAt = DateTime.UtcNow,
            Status = AuditStatus.Closed,
            Findings = "ok"
        });
        var job = new PayrollRunJob
        {
            PayrollRunId = run.Id,
            Status = PayrollRunJobStatus.Running,
            Phase = PayrollRunJobPhase.Calculate,
            ResultsValidated = false
        };
        job.Partitions.Add(new PayrollRunPartition
        {
            PartitionIndex = 0,
            Status = PayrollRunPartitionStatus.Running,
            EmployeeCount = 2,
            ProcessedCount = 1
        });
        db.PayrollRunJobs.Add(job);
        await db.SaveChangesAsync();

        var (ok, err) = await mediator.Send(new PostPayrollRunCommand(run.Id, Guid.NewGuid()));
        Assert.False(ok);
        Assert.True(err!.Contains("تحقق") || err.Contains("أقسام") || err.Contains("تجميد"));
    }

    [Fact]
    public async Task Snapshot_VersionConflict_Detected()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        await SeedPublishedConfigAsync(db);
        var emp = Guid.NewGuid();
        await SeedEmployeesAsync(db, emp);

        var (created, runId, _) = await mediator.Send(new CreatePayrollRunCommand(
            new DateTime(2026, 9, 1), new DateTime(2026, 9, 30), DateTime.UtcNow));
        Assert.True(created);

        // Start job by creating snapshot then mutate profile before process — use pause path:
        // Create job with snapshot, mutate profile version, resume process
        var run = await db.PayrollRuns.FirstAsync(r => r.Id == runId);
        run.BeginCalculating();
        var config = await db.PayrollConfigurations.FirstAsync();
        var profile = await db.EmployeePayProfiles.FirstAsync(p => p.EmployeeId == emp);
        var job = new PayrollRunJob
        {
            PayrollRunId = runId!.Value,
            Status = PayrollRunJobStatus.Paused,
            Phase = PayrollRunJobPhase.Calculate,
            PartitionSize = 200,
            Attempt = 1
        };
        job.InputSnapshots.Add(new PayrollRunInputSnapshot
        {
            EmployeeId = emp,
            EmployeePayProfileId = profile.Id,
            ProfileVersion = "stale-version",
            PayrollConfigurationId = config.Id,
            ConfigContentHash = config.ContentHash!,
            SortOrder = 0
        });
        job.Partitions.Add(new PayrollRunPartition
        {
            PartitionIndex = 0,
            Status = PayrollRunPartitionStatus.Pending,
            EmployeeCount = 1
        });
        db.PayrollRunJobs.Add(job);
        await db.SaveChangesAsync();

        var resume = await mediator.Send(new ResumePayrollRunJobCommand(job.Id));
        Assert.False(resume.Success);
        Assert.Contains("تعارض إصدار", resume.Error!);
    }

    [Fact]
    public async Task Reverse_CreatesCompensatingBatch()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 10, 1),
            PeriodEnd = new DateTime(2026, 10, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Posted,
            TotalNetPay = 900m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true
        };
        run.SalaryLines.Add(new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            NetPay = 900m
        });
        db.PayrollRuns.Add(run);
        var original = new PaymentBatch
        {
            PayrollRunId = run.Id,
            TotalAmount = 900m,
            Status = PaymentBatchStatus.Processed,
            BatchKind = PaymentBatchKind.Disbursement,
            ProcessedAt = DateTime.UtcNow,
            BankTransferReference = "PAY-ORIG"
        };
        db.PaymentBatches.Add(original);
        await db.SaveChangesAsync();

        var (revOk, revErr) = await mediator.Send(new ReversePayrollRunCommand(run.Id));
        Assert.True(revOk, revErr);

        var batches = await db.PaymentBatches.Where(b => b.PayrollRunId == run.Id).ToListAsync();
        Assert.Contains(batches, b => b.BatchKind == PaymentBatchKind.Reversal);
        Assert.Contains(batches, b => b.Status == PaymentBatchStatus.Reversed && b.BatchKind == PaymentBatchKind.Disbursement);
        Assert.Equal(PayrollRunStatus.Reversed, (await db.PayrollRuns.FindAsync(run.Id))!.Status);
        Assert.Equal(1, await db.SalaryLines.CountAsync(l => l.PayrollRunId == run.Id)); // append-only
    }

    [Fact]
    public async Task Post_CreatesPendingBatch_Pay_Processes()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var approverId = Guid.NewGuid();
        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 11, 1),
            PeriodEnd = new DateTime(2026, 11, 30),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Approved,
            TotalNetPay = 900m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true
        };
        run.SalaryLines.Add(new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = Guid.NewGuid(),
            GrossPay = 1000m,
            TotalDeductions = 100m,
            NetPay = 900m
        });
        run.Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = run.Id,
            ApproverId = approverId,
            Decision = ApprovalDecision.Approved,
            DecidedAt = DateTime.UtcNow
        });
        db.PayrollRuns.Add(run);
        db.PayrollAuditRecords.Add(new PayrollAuditRecord
        {
            PayrollRunId = run.Id,
            ClosedBy = Guid.NewGuid(),
            ClosedAt = DateTime.UtcNow,
            Status = AuditStatus.Closed,
            Findings = "ok"
        });
        await db.SaveChangesAsync();

        var poster = Guid.NewGuid();
        var (posted, postErr) = await mediator.Send(new PostPayrollRunCommand(run.Id, poster));
        Assert.True(posted, postErr);

        var batch = await db.PaymentBatches.FirstAsync(b => b.PayrollRunId == run.Id);
        Assert.Equal(PaymentBatchStatus.Pending, batch.Status);
        Assert.Null(batch.ProcessedAt);

        // Paying submits the batch to the channel. It must not report money as paid here: this
        // step used to write Processed straight to the database with no transfer attempted.
        var (payOk, payErr) = await mediator.Send(new ProcessPaymentBatchCommand(batch.Id));
        Assert.True(payOk, payErr);

        db.ChangeTracker.Clear();
        var submitted = await db.PaymentBatches.FindAsync(batch.Id);
        Assert.Equal(PaymentBatchStatus.Submitted, submitted!.Status);
        Assert.NotNull(submitted.SubmittedAt);
        Assert.Null(submitted.SettledAt);
        Assert.False(PaymentBatchTransitions.IsPaid(submitted.Status));

        var (ackOk, ackErr) = await mediator.Send(new AckPaymentBatchCommand(batch.Id, "ACK-1"));
        Assert.True(ackOk, ackErr);

        var (settleOk, settleErr) = await mediator.Send(
            new SettlePaymentBatchCommand(batch.Id, "SETTLE-REF-1", 900m));
        Assert.True(settleOk, settleErr);

        db.ChangeTracker.Clear();
        var settled = await db.PaymentBatches.FindAsync(batch.Id);
        Assert.Equal(PaymentBatchStatus.Settled, settled!.Status);
        Assert.Equal("SETTLE-REF-1", settled.SettlementReference);
        Assert.NotNull(settled.SettledAt);
        Assert.True(PaymentBatchTransitions.IsPaid(settled.Status));
    }

    [Fact]
    public async Task Settle_WithoutSettlementReference_IsRejected()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 12, 1),
            PeriodEnd = new DateTime(2026, 12, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Posted,
            TotalNetPay = 500m
        };
        db.PayrollRuns.Add(run);
        var batch = new PaymentBatch
        {
            PayrollRunId = run.Id,
            TotalAmount = 500m,
            Status = PaymentBatchStatus.Acked
        };
        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync();

        var (ok, err) = await mediator.Send(new SettlePaymentBatchCommand(batch.Id, "  ", 500m));

        Assert.False(ok);
        Assert.NotNull(err);
        db.ChangeTracker.Clear();
        Assert.Equal(PaymentBatchStatus.Acked, (await db.PaymentBatches.FindAsync(batch.Id))!.Status);
    }
}
