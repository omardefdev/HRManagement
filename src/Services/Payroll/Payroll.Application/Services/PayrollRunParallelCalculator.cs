using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using HRManagement.SharedKernel.Legal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Abstractions;
using Payroll.Application.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Domain.Services.Engine;

namespace Payroll.Application.Services;

/// <summary>PERF-001: bounded Channel workers with bulk read/write per partition.</summary>
public static class PayrollRunParallelCalculator
{
    public static async Task<(bool Success, int LinesGenerated, Guid? JobId, string? Error)> RunAsync(
        IPayrollRepository coordinatorRepository,
        IServiceScopeFactory scopeFactory,
        IPayrollEngine engine,
        PayrollBatchOptions options,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var repository = coordinatorRepository;

        var job = await repository.PayrollRunJobs
            .Include(j => j.Partitions)
            .Include(j => j.InputSnapshots)
            .FirstAsync(j => j.Id == jobId, cancellationToken);

        var run = await repository.GetPayrollRunAsync(job.PayrollRunId, includeLines: true, cancellationToken);
        if (run is null) return (false, 0, jobId, "التشغيل غير موجود.");

        var periodStart = DateOnly.FromDateTime(run.PeriodStart);
        var periodEnd = DateOnly.FromDateTime(run.PeriodEnd);
        var configResult = await ResolveConfigAsync(repository, run, periodStart, cancellationToken);
        if (configResult.Error != null) return (false, 0, jobId, configResult.Error);
        var config = configResult.Config!;

        var employeeIds = job.InputSnapshots.Select(s => s.EmployeeId).Distinct().ToList();
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var hrCoreClient = scope.ServiceProvider.GetService<IHrCoreServiceRegimeClient>();
            var configuration = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var blocksFinalPayroll = configuration?.GetValue("ServiceRegimes:BlocksFinalPayroll", true) ?? true;
            var regimeError = await PayrollRegimeGovernance.ValidateEmployeesAsync(
                run.RunType,
                employeeIds,
                hrCoreClient,
                blocksFinalPayroll,
                cancellationToken);
            if (regimeError is not null)
                return (false, 0, jobId, regimeError);
        }

        var profileIds = job.InputSnapshots.OrderBy(s => s.SortOrder)
            .Select(s => s.EmployeePayProfileId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();
        var legacyEmployeeIds = job.InputSnapshots
            .Where(s => !s.EmployeePayProfileId.HasValue)
            .Select(s => s.EmployeeId)
            .Distinct()
            .ToList();
        var profiles = await repository.EmployeePayProfiles
            .Where(p => profileIds.Contains(p.Id)
                || (legacyEmployeeIds.Contains(p.EmployeeId) && p.IsActive))
            .Include(p => p.PayGrade)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var profileById = profiles.ToDictionary(p => p.Id);
        var legacyProfileByEmployee = profiles
            .Where(p => legacyEmployeeIds.Contains(p.EmployeeId))
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.UpdatedAt).First());

        var holidays = await repository.OfficialHolidays
            .Where(h => h.PayrollConfigurationId == null || h.PayrollConfigurationId == config.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var extraHolidays = PayrollRunCalculationSupport.BuildHolidaySet(periodStart, periodEnd, holidays);
        var deductionTypes = await repository.DeductionTypes.AsNoTracking().ToListAsync(cancellationToken);

        job.Phase = PayrollRunJobPhase.Calculate;
        job.Status = PayrollRunJobStatus.Running;
        job.TotalEmployeeCount = job.InputSnapshots.Count;
        job.CalculateStartedAt ??= DateTime.UtcNow;
        job.ProcessedEmployeeCount = Math.Max(job.ProcessedEmployeeCount, 0);
        await repository.SaveChangesAsync(cancellationToken);

        var snapshotsByOrder = job.InputSnapshots.OrderBy(s => s.SortOrder).ToList();
        var pendingPartitions = job.Partitions
            .Where(p => p.Status != PayrollRunPartitionStatus.Completed
                        && p.Status != PayrollRunPartitionStatus.Cancelled)
            .OrderBy(p => p.PartitionIndex)
            .Select(p => p.Id)
            .ToList();

        var dop = Math.Clamp(options.MaxDegreeOfParallelism, 1, 32);
        var capacity = Math.Clamp(options.PartitionQueueCapacity, 1, 256);
        var bulkSize = Math.Clamp(options.BulkWriteSize, 1, 500);

        var channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = false
        });

        var linesGenerated = 0;
        var processedEmployees = job.ProcessedEmployeeCount;
        string? fatalError = null;
        var fatalGate = new object();
        var stopRequested = 0;

        var writerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var pid in pendingPartitions)
                {
                    if (Volatile.Read(ref stopRequested) != 0) break;
                    await channel.Writer.WriteAsync(pid, cancellationToken);
                }
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, cancellationToken);

        var workers = Enumerable.Range(0, dop).Select(i => Task.Run(async () =>
        {
            var workerId = $"{Environment.MachineName}-{Environment.ProcessId}-w{i}";
            await foreach (var partitionId in channel.Reader.ReadAllAsync(cancellationToken))
            {
                if (Volatile.Read(ref stopRequested) != 0) break;

                AsyncServiceScope? workerScope = dop > 1 ? scopeFactory.CreateAsyncScope() : null;
                try
                {
                    var repo = workerScope is not null
                        ? workerScope.Value.ServiceProvider.GetRequiredService<IPayrollRepository>()
                        : repository;

                    var liveJob = await repo.PayrollRunJobs.AsNoTracking()
                        .FirstAsync(j => j.Id == jobId, cancellationToken);
                    if (liveJob.Status == PayrollRunJobStatus.Paused)
                    {
                        Interlocked.Exchange(ref stopRequested, 1);
                        break;
                    }
                    if (liveJob.Status == PayrollRunJobStatus.Cancelled)
                    {
                        Interlocked.Exchange(ref stopRequested, 2);
                        break;
                    }

                    if (!await repo.TryClaimPartitionAsync(partitionId, workerId, cancellationToken))
                        continue;

                    PayrollRunMetrics.ActivePartitions.Add(1);
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var partition = await repo.PayrollRunPartitions.FirstAsync(p => p.Id == partitionId, cancellationToken);
                        var slice = snapshotsByOrder
                            .Skip(partition.PartitionIndex * job.PartitionSize)
                            .Take(job.PartitionSize)
                            .ToList();

                        var bag = await PayrollPartitionInputLoader.LoadAsync(
                            repo, slice.Select(s => s.EmployeeId).ToList(), run, periodStart, periodEnd, cancellationToken);

                        var packages = new List<PayrollEmployeeCalculationPackage>(bulkSize);
                        Guid? lastEmployee = null;
                        var partitionProcessed = partition.ProcessedCount;

                        async Task FlushAsync()
                        {
                            if (packages.Count == 0) return;
                            var (saved, _) = await repo.SaveEmployeePackagesBulkAsync(run.Id, packages, cancellationToken);
                            Interlocked.Add(ref linesGenerated, saved);
                            packages.Clear();
                            repo.ClearChangeTracker();

                            partition = await repo.PayrollRunPartitions.FirstAsync(p => p.Id == partitionId, cancellationToken);
                            partition.ProcessedCount = partitionProcessed;
                            partition.CursorEmployeeId = lastEmployee;
                            var jobRow = await repo.PayrollRunJobs.FirstAsync(j => j.Id == jobId, cancellationToken);
                            jobRow.LastCheckpointAt = DateTime.UtcNow;
                            jobRow.ProcessedEmployeeCount = Volatile.Read(ref processedEmployees);
                            UpdateEta(jobRow);
                            await repo.SaveChangesAsync(cancellationToken);
                            repo.ClearChangeTracker();
                        }

                        foreach (var snap in slice)
                        {
                            liveJob = await repo.PayrollRunJobs.AsNoTracking()
                                .FirstAsync(j => j.Id == jobId, cancellationToken);
                            if (liveJob.Status == PayrollRunJobStatus.Paused)
                            {
                                await FlushAsync();
                                partition = await repo.PayrollRunPartitions.FirstAsync(p => p.Id == partitionId, cancellationToken);
                                partition.CursorEmployeeId = snap.EmployeeId;
                                await repo.SaveChangesAsync(cancellationToken);
                                Interlocked.Exchange(ref stopRequested, 1);
                                return;
                            }
                            if (liveJob.Status == PayrollRunJobStatus.Cancelled)
                            {
                                partition = await repo.PayrollRunPartitions.FirstAsync(p => p.Id == partitionId, cancellationToken);
                                partition.Status = PayrollRunPartitionStatus.Cancelled;
                                await repo.SaveChangesAsync(cancellationToken);
                                Interlocked.Exchange(ref stopRequested, 2);
                                return;
                            }

                            EmployeePayProfile? profile;
                            var profileFound = snap.EmployeePayProfileId is Guid pinnedProfileId
                                ? profileById.TryGetValue(pinnedProfileId, out profile)
                                : legacyProfileByEmployee.TryGetValue(snap.EmployeeId, out profile);
                            if (!profileFound || profile is null)
                            {
                                await FailPartitionAsync(repo, partitionId, jobId,
                                    $"الملف غير موجود للموظف {snap.EmployeeId:N}.", cancellationToken);
                                Interlocked.Exchange(ref stopRequested, 3);
                                lock (fatalGate) { fatalError ??= $"الملف غير موجود للموظف {snap.EmployeeId:N}."; }
                                return;
                            }

                            var currentVersion = PayrollEmployeeCalculationUnit.ComputeProfileVersion(profile);
                            if (!string.Equals(snap.ProfileVersion, currentVersion, StringComparison.Ordinal))
                            {
                                var msg = $"تعارض إصدار ملف الراتب للموظف {profile.EmployeeId:N}.";
                                await FailPartitionAsync(repo, partitionId, jobId, msg, cancellationToken);
                                Interlocked.Exchange(ref stopRequested, 3);
                                lock (fatalGate) { fatalError ??= msg; }
                                return;
                            }

                            var allowances = bag.AllowancesFor(snap.EmployeeId);
                            var deductions = bag.DeductionsFor(snap.EmployeeId);
                            var adjustments = bag.AdjustmentsFor(snap.EmployeeId);
                            var absences = bag.AbsencesFor(snap.EmployeeId);
                            var fingerprint = PayrollInputFingerprint.Compute(allowances, deductions, adjustments, absences);
                            if (!string.IsNullOrEmpty(snap.InputFingerprint)
                                && !string.Equals(snap.InputFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                            {
                                var msg = $"تعارض بصمة مدخلات الراتب للموظف {profile.EmployeeId:N}.";
                                await FailPartitionAsync(repo, partitionId, jobId, msg, cancellationToken);
                                Interlocked.Exchange(ref stopRequested, 3);
                                lock (fatalGate) { fatalError ??= msg; }
                                PayrollRunMetrics.EmployeesFailed.Add(1);
                                return;
                            }

                            lastEmployee = profile.EmployeeId;
                            if (bag.ExistingLineEmployeeIds.Contains(profile.EmployeeId))
                            {
                                partitionProcessed++;
                                Interlocked.Increment(ref processedEmployees);
                                PayrollRunMetrics.EmployeesProcessed.Add(1);
                                continue;
                            }

                            var package = PayrollEmployeeCalculationUnit.Build(
                                engine, run, profile, profile.PayGrade, config, extraHolidays,
                                allowances, deductions, deductionTypes, adjustments, absences);
                            packages.Add(package);
                            partitionProcessed++;
                            Interlocked.Increment(ref processedEmployees);
                            PayrollRunMetrics.EmployeesProcessed.Add(1);

                            if (packages.Count >= bulkSize)
                                await FlushAsync();
                        }

                        await FlushAsync();

                        partition = await repo.PayrollRunPartitions.FirstAsync(p => p.Id == partitionId, cancellationToken);
                        partition.Status = PayrollRunPartitionStatus.Completed;
                        partition.ProcessedCount = slice.Count;
                        partition.WorkerId = workerId;
                        await repo.SaveChangesAsync(cancellationToken);
                    }
                    finally
                    {
                        PayrollRunMetrics.ActivePartitions.Add(-1);
                        PayrollRunMetrics.PartitionDurationMs.Record(sw.Elapsed.TotalMilliseconds);
                    }
                }
                finally
                {
                    if (workerScope is not null)
                        await workerScope.Value.DisposeAsync();
                }
            }
        }, cancellationToken)).ToArray();

        await Task.WhenAll(workers.Append(writerTask));

        // Reload on coordinator context (same request scope that started the job).
        repository.ClearChangeTracker();
        var finalJob = await repository.PayrollRunJobs
            .Include(j => j.Partitions)
            .Include(j => j.InputSnapshots)
            .FirstAsync(j => j.Id == jobId, cancellationToken);
        var finalRun = await repository.GetPayrollRunAsync(finalJob.PayrollRunId, includeLines: true, cancellationToken);
        if (finalRun is null) return (false, linesGenerated, jobId, "التشغيل غير موجود.");

        finalJob.ProcessedEmployeeCount = Volatile.Read(ref processedEmployees);
        finalJob.LastCheckpointAt = DateTime.UtcNow;

        var stop = Volatile.Read(ref stopRequested);
        // Pause is a durable operator decision. A worker can finish its last item
        // between the final status check and this coordinator reload, so honor the
        // persisted pause before any validation/finalization can mark the job complete.
        if (stop == 1 || finalJob.Status == PayrollRunJobStatus.Paused)
        {
            finalJob.Status = PayrollRunJobStatus.Paused;
            await repository.SaveChangesAsync(cancellationToken);
            repository.ClearChangeTracker();
            return (true, linesGenerated, jobId, null);
        }
        if (stop == 2)
        {
            finalRun.MarkCancelled();
            finalJob.Status = PayrollRunJobStatus.Cancelled;
            await repository.SaveChangesAsync(cancellationToken);
            repository.ClearChangeTracker();
            return (false, linesGenerated, jobId, "أُلغيت مهمة التشغيل.");
        }
        if (stop == 3 || fatalError != null)
        {
            finalJob.Status = PayrollRunJobStatus.Failed;
            finalJob.Error ??= fatalError;
            finalRun.Status = PayrollRunStatus.Calculating;
            await repository.SaveChangesAsync(cancellationToken);
            repository.ClearChangeTracker();
            return (false, linesGenerated, jobId, fatalError ?? finalJob.Error);
        }

        if (finalJob.Partitions.Any(p => p.Status != PayrollRunPartitionStatus.Completed))
        {
            finalJob.Status = PayrollRunJobStatus.Paused;
            await repository.SaveChangesAsync(cancellationToken);
            repository.ClearChangeTracker();
            return (true, linesGenerated, jobId, null);
        }

        finalJob.Phase = PayrollRunJobPhase.Validate;
        finalRun.Status = PayrollRunStatus.Validating;
        await repository.SaveChangesAsync(cancellationToken);

        var lines = await repository.SalaryLines
            .Where(l => l.PayrollRunId == finalRun.Id)
            .ToListAsync(cancellationToken);
        var lineIds = lines.Select(l => l.Id).ToList();
        var comps = await repository.SalaryLineComponents
            .Where(c => lineIds.Contains(c.SalaryLineId))
            .ToListAsync(cancellationToken);
        var byLine = comps.GroupBy(c => c.SalaryLineId).ToDictionary(g => g.Key, g => g.ToList());

        var totalNet = PayrollRunReconciliation.SumNet(lines);
        var (ok, err) = PayrollRunReconciliation.ValidateLineIntegrity(
            lines, byLine, expectedEmployeeCount: finalJob.InputSnapshots.Count);
        if (!ok)
        {
            finalJob.Status = PayrollRunJobStatus.Failed;
            finalJob.Error = err;
            finalRun.Status = PayrollRunStatus.Calculating;
            await repository.SaveChangesAsync(cancellationToken);
            repository.ClearChangeTracker();
            return (false, linesGenerated, jobId, err);
        }

        finalRun.MarkValidated(totalNet);
        finalJob.ResultsValidated = true;
        finalJob.Status = PayrollRunJobStatus.Completed;
        finalJob.EstimatedCompletionAt = DateTime.UtcNow;
        finalJob.LastCheckpointAt = DateTime.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        repository.ClearChangeTracker();

        return (true, linesGenerated, jobId, null);
    }

    private static void UpdateEta(PayrollRunJob job)
    {
        if (job.CalculateStartedAt is null || job.ProcessedEmployeeCount <= 0 || job.TotalEmployeeCount <= 0)
            return;
        var elapsed = DateTime.UtcNow - job.CalculateStartedAt.Value;
        if (elapsed.TotalSeconds < 0.5) return;
        var rate = job.ProcessedEmployeeCount / elapsed.TotalSeconds;
        if (rate <= 0) return;
        var remaining = job.TotalEmployeeCount - job.ProcessedEmployeeCount;
        job.EstimatedCompletionAt = DateTime.UtcNow.AddSeconds(remaining / rate);
    }

    private static async Task FailPartitionAsync(
        IPayrollRepository repo,
        Guid partitionId,
        Guid jobId,
        string error,
        CancellationToken ct)
    {
        var partition = await repo.PayrollRunPartitions.FirstAsync(p => p.Id == partitionId, ct);
        partition.Status = PayrollRunPartitionStatus.Failed;
        partition.Error = error;
        var job = await repo.PayrollRunJobs.FirstAsync(j => j.Id == jobId, ct);
        job.Status = PayrollRunJobStatus.Failed;
        job.Error = error;
        await repo.SaveChangesAsync(ct);
    }

    private static async Task<(PayrollConfiguration? Config, string? Error)> ResolveConfigAsync(
        IPayrollRepository repository,
        PayrollRun run,
        DateOnly periodStart,
        CancellationToken cancellationToken)
    {
        if (run.PayrollConfigurationId.HasValue)
        {
            var pinned = await repository.GetConfigurationByIdAsync(run.PayrollConfigurationId.Value, cancellationToken);
            if (pinned is null)
                return (null, "نسخة الإعدادات المثبتة على التشغيل غير موجودة.");
            if (string.IsNullOrEmpty(pinned.ContentHash))
                return (null, "نسخة الإعدادات المثبتة بلا بصمة محتوى قانونية.");
            if (!string.IsNullOrEmpty(run.ConfigContentHash)
                && !string.Equals(run.ConfigContentHash, pinned.ContentHash, StringComparison.OrdinalIgnoreCase))
                return (null, "بصمة الإعدادات المثبتة لا تطابق النسخة القانونية.");
            return (pinned, null);
        }

        var published = await repository.GetPublishedConfigurationForPeriodAsync(
            periodStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), cancellationToken);
        if (published is null || string.IsNullOrEmpty(published.ContentHash))
            return (null, "لا توجد نسخة إعدادات معتمدة/منشورة للفترة.");
        return (published, null);
    }
}
