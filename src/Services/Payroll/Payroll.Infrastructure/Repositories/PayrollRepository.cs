using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Repositories;

public class PayrollRepository(PayrollDbContext db, IPayrollCalendar? calendar = null) : IPayrollRepository
{
    /// <summary>«Today» when no calendar is injected (a test that builds the repository by hand): the system clock in the default zone.</summary>
    private static readonly IPayrollCalendar DefaultCalendar = new PayrollCalendar(TimeProvider.System);

    public IQueryable<PayrollRun> PayrollRuns => db.PayrollRuns;
    public IQueryable<EmployeePayProfile> EmployeePayProfiles => db.EmployeePayProfiles;
    public IQueryable<EmployeePayProfileHistory> EmployeePayProfileHistories => db.EmployeePayProfileHistories;
    public IQueryable<PayProfileChangeRequest> PayProfileChangeRequests => db.PayProfileChangeRequests;
    public IQueryable<EmployeeBankAccount> EmployeeBankAccounts => db.EmployeeBankAccounts;
    public IQueryable<PayrollReferenceChangeRequest> PayrollReferenceChangeRequests => db.PayrollReferenceChangeRequests;
    public IQueryable<SalaryScale> SalaryScales => db.SalaryScales;
    public IQueryable<AllowanceRecord> AllowanceRecords => db.AllowanceRecords;
    public IQueryable<DeductionRecord> DeductionRecords => db.DeductionRecords;
    public IQueryable<DeductionType> DeductionTypes => db.DeductionTypes;
    public IQueryable<PayrollAdjustment> PayrollAdjustments => db.PayrollAdjustments;
    public IQueryable<PayrollConfiguration> PayrollConfigurations => db.PayrollConfigurations;
    public IQueryable<OfficialHoliday> OfficialHolidays => db.OfficialHolidays;
    public IQueryable<AbsenceDeduction> AbsenceDeductions => db.AbsenceDeductions;
    public IQueryable<SalaryLine> SalaryLines => db.SalaryLines;
    public IQueryable<SalaryLineComponent> SalaryLineComponents => db.SalaryLineComponents;
    public IQueryable<EndOfServiceSettlement> EndOfServiceSettlements => db.EndOfServiceSettlements;
    public IQueryable<PayslipDelivery> PayslipDeliveries => db.PayslipDeliveries;
    public IQueryable<PayGrade> PayGrades => db.PayGrades;
    public IQueryable<PayrollAuditRecord> PayrollAuditRecords => db.PayrollAuditRecords;
    public IQueryable<PayrollRunJob> PayrollRunJobs => db.PayrollRunJobs;
    public IQueryable<PayrollRunPartition> PayrollRunPartitions => db.PayrollRunPartitions;
    public IQueryable<PayrollRunInputSnapshot> PayrollRunInputSnapshots => db.PayrollRunInputSnapshots;
    public IQueryable<PaymentBatch> PaymentBatches => db.PaymentBatches;
    public IQueryable<PaymentOrder> PaymentOrders => db.PaymentOrders;
    public IQueryable<FundingDecision> FundingDecisions => db.FundingDecisions;
    public IQueryable<DisbursementPriorityRule> DisbursementPriorityRules => db.DisbursementPriorityRules;
    public IQueryable<FraudAlert> FraudAlerts => db.FraudAlerts;
    public IQueryable<PaymentHold> PaymentHolds => db.PaymentHolds;

    public Task<PayrollRun?> GetPayrollRunAsync(Guid id, bool includeLines, CancellationToken ct)
    {
        var q = db.PayrollRuns
            .Include(r => r.Approvals)
            .AsQueryable();
        if (includeLines) q = q.Include(r => r.SalaryLines);
        return q.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public IQueryable<LegalDecisionRecord> LegalDecisionRecords => db.LegalDecisionRecords;
    public IQueryable<CompensationType> CompensationTypes => db.CompensationTypes;
    public IQueryable<LegalAttestation> LegalAttestations => db.LegalAttestations;
    public IQueryable<PayrollRunRevision> PayrollRunRevisions => db.PayrollRunRevisions;
    public IQueryable<PayrollReversalRequest> PayrollReversalRequests => db.PayrollReversalRequests;
    public IQueryable<PayrollRunApproval> PayrollRunApprovals => db.PayrollRunApprovals;

    /// <summary>Every table of a version that governs pay.</summary>
    private IQueryable<PayrollConfiguration> WithContent() =>
        db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .Include(c => c.StatutoryDeductions)
            .Include(c => c.Holidays)
            .Include(c => c.PriorityRules)
            .Include(c => c.FieldSources)
            .AsSplitQuery();

    /// <summary>
    /// PLAN-54 (P-6): the version that governs <paramref name="day"/>, among every version that was ever published (a superseded
    /// version still governs its own dates). The choice is made in memory by <see cref="PayrollConfigurationVersioning.ResolveGoverning"/>
    /// over the agency's handful of versions, then that one version is loaded with its tables.
    /// </summary>
    private async Task<PayrollConfiguration?> GoverningAsync(DateTime day, string? regimeOrCode, CancellationToken ct)
    {
        var candidates = await db.PayrollConfigurations
            .AsNoTracking()
            .Where(c => c.PublicationStatus == PayrollConfigPublicationStatus.Published
                        || c.PublicationStatus == PayrollConfigPublicationStatus.Superseded)
            .ToListAsync(ct);
        var governing = PayrollConfigurationVersioning.ResolveGoverning(candidates, day, regimeOrCode);
        return governing is null ? null : await GetConfigurationByIdAsync(governing.Id, ct);
    }

    private DateTime Today() => (calendar ?? DefaultCalendar).Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public Task<PayrollConfiguration?> GetActiveConfigurationAsync(CancellationToken ct) =>
        GoverningAsync(Today(), null, ct);

    public Task<PayrollConfiguration?> GetConfigurationByRegimeCodeAsync(string regimeCode, CancellationToken ct) =>
        GoverningAsync(Today(), regimeCode, ct);

    public Task<PayrollConfiguration?> GetPublishedConfigurationForPeriodAsync(
        DateTime periodStart,
        CancellationToken ct) => GoverningAsync(periodStart, null, ct);

    public Task<PayrollConfiguration?> GetConfigurationByIdAsync(Guid id, CancellationToken ct) =>
        WithContent()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<PayrollConfiguration?> GetFrameworkForUpdateAsync(Guid id, CancellationToken ct) =>
        WithContent()
            .Include(c => c.Editors)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<EmployeePayProfile?> GetActiveProfileAsync(Guid employeeId, CancellationToken ct) =>
        db.EmployeePayProfiles
            .Include(p => p.PayGrade)
            .FirstOrDefaultAsync(p => p.EmployeeId == employeeId && p.IsActive, ct);

    public Task<PayrollRunJob?> GetActiveJobAsync(Guid payrollRunId, CancellationToken ct) =>
        db.PayrollRunJobs
            .Include(j => j.Partitions)
            .Include(j => j.InputSnapshots)
            .Where(j => j.PayrollRunId == payrollRunId
                        && (j.Status == PayrollRunJobStatus.Pending
                            || j.Status == PayrollRunJobStatus.Running
                            || j.Status == PayrollRunJobStatus.Paused))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync<T>(T entity, CancellationToken ct) where T : class =>
        await db.Set<T>().AddAsync(entity, ct);

    public void Remove<T>(T entity) where T : class => db.Set<T>().Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct) =>
        db.Database.BeginTransactionAsync(ct);

    public void ClearChangeTracker() => db.ChangeTracker.Clear();

    public void MarkAsAdded<T>(T entity) where T : class
    {
        var entry = db.Entry(entity);
        if (entry.State != EntityState.Added)
            entry.State = EntityState.Added;
    }

    public Task<(bool Saved, bool AlreadyExists)> SaveEmployeePackageAtomicAsync(
        Guid payrollRunId,
        PayrollEmployeeCalculationPackage package,
        CancellationToken ct) =>
        SaveOnePackageAsync(payrollRunId, runningJobId: null, package, ct);

    /// <summary>The marker of a save refused because its job is no longer running: nothing was written.</summary>
    private sealed class JobNoLongerRunningException : Exception;

    /// <summary>
    /// PLAN-56: true while <paramref name="jobId"/> is running. Called inside the save's transaction; on PostgreSQL it takes a share
    /// lock on the job's row, which a cancellation's update of that row waits for, and which waits for a cancellation in flight.
    /// </summary>
    private async Task<bool> JobIsRunningAsync(Guid jobId, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
        {
            var running = await db.Database
                .SqlQuery<int>($"""SELECT 1 AS "Value" FROM "PayrollRunJobs" WHERE "Id" = {jobId} AND "Status" = 'Running' FOR SHARE""")
                .ToListAsync(ct);
            return running.Count > 0;
        }

        return await db.PayrollRunJobs.AsNoTracking().AnyAsync(j => j.Id == jobId && j.Status == PayrollRunJobStatus.Running, ct);
    }

    private async Task<(bool Saved, bool AlreadyExists)> SaveOnePackageAsync(
        Guid payrollRunId,
        Guid? runningJobId,
        PayrollEmployeeCalculationPackage package,
        CancellationToken ct)
    {
        if (await db.SalaryLines.AnyAsync(
                l => l.PayrollRunId == payrollRunId && l.EmployeeId == package.Line.EmployeeId, ct))
            return (false, true);

        var ownsTx = db.Database.CurrentTransaction is null;
        await using var tx = ownsTx ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            if (runningJobId is Guid jobId && !await JobIsRunningAsync(jobId, ct))
                throw new JobNoLongerRunningException();

            await db.SalaryLines.AddAsync(package.Line, ct);
            await db.SaveChangesAsync(ct);

            foreach (var c in package.Components)
            {
                c.SalaryLineId = package.Line.Id;
                await db.SalaryLineComponents.AddAsync(c, ct);
            }

            foreach (var d in package.DeductionsToApply)
            {
                var tracked = db.DeductionRecords.Local.FirstOrDefault(x => x.Id == d.Id)
                              ?? await db.DeductionRecords.FirstAsync(x => x.Id == d.Id, ct);
                tracked.AppliedInRunId = payrollRunId;
            }
            foreach (var a in package.AdjustmentsToApply)
            {
                var tracked = db.PayrollAdjustments.Local.FirstOrDefault(x => x.Id == a.Id)
                              ?? await db.PayrollAdjustments.FirstAsync(x => x.Id == a.Id, ct);
                tracked.PayrollRunId = payrollRunId;
            }
            foreach (var ab in package.AbsencesToApply)
            {
                var tracked = db.AbsenceDeductions.Local.FirstOrDefault(x => x.Id == ab.Id)
                              ?? await db.AbsenceDeductions.FirstAsync(x => x.Id == ab.Id, ct);
                tracked.AppliedInRunId = payrollRunId;
            }

            await db.SaveChangesAsync(ct);
            if (tx is not null)
                await tx.CommitAsync(ct);
            return (true, false);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true
            || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            if (tx is not null)
                await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return (false, true);
        }
        catch
        {
            if (tx is not null)
                await tx.RollbackAsync(ct);
            throw;
        }
    }

    public Task<(int Saved, int AlreadyExists)> SaveEmployeePackagesBulkAsync(
        Guid payrollRunId,
        IReadOnlyList<PayrollEmployeeCalculationPackage> packages,
        CancellationToken ct) =>
        SavePackagesAsync(payrollRunId, runningJobId: null, packages, ct);

    public async Task<(int Saved, int AlreadyExists)?> SaveEmployeePackagesForJobAsync(
        Guid payrollRunId,
        Guid jobId,
        IReadOnlyList<PayrollEmployeeCalculationPackage> packages,
        CancellationToken ct)
    {
        try
        {
            return await SavePackagesAsync(payrollRunId, jobId, packages, ct);
        }
        catch (JobNoLongerRunningException)
        {
            // The transaction of the refused save was rolled back; nothing of these packages is tracked or written.
            db.ChangeTracker.Clear();
            return null;
        }
    }

    private async Task<(int Saved, int AlreadyExists)> SavePackagesAsync(
        Guid payrollRunId,
        Guid? runningJobId,
        IReadOnlyList<PayrollEmployeeCalculationPackage> packages,
        CancellationToken ct)
    {
        if (packages.Count == 0) return (0, 0);

        var employeeIds = packages.Select(p => p.Line.EmployeeId).ToList();
        var existing = await db.SalaryLines
            .AsNoTracking()
            .Where(l => l.PayrollRunId == payrollRunId && employeeIds.Contains(l.EmployeeId))
            .Select(l => l.EmployeeId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();

        var toSave = packages.Where(p => !existingSet.Contains(p.Line.EmployeeId)).ToList();
        var already = packages.Count - toSave.Count;
        if (toSave.Count == 0) return (0, already);

        // Prefer ambient transaction when present (avoids SQLite nested-tx on shared connections).
        var ownsTx = db.Database.CurrentTransaction is null;
        await using var tx = ownsTx ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            if (runningJobId is Guid jobId && !await JobIsRunningAsync(jobId, ct))
                throw new JobNoLongerRunningException();

            foreach (var package in toSave)
                await db.SalaryLines.AddAsync(package.Line, ct);
            await db.SaveChangesAsync(ct);

            foreach (var package in toSave)
            {
                foreach (var c in package.Components)
                {
                    c.SalaryLineId = package.Line.Id;
                    await db.SalaryLineComponents.AddAsync(c, ct);
                }

                foreach (var d in package.DeductionsToApply)
                {
                    var tracked = db.DeductionRecords.Local.FirstOrDefault(x => x.Id == d.Id)
                                  ?? await db.DeductionRecords.FirstAsync(x => x.Id == d.Id, ct);
                    tracked.AppliedInRunId = payrollRunId;
                }

                foreach (var a in package.AdjustmentsToApply)
                {
                    var tracked = db.PayrollAdjustments.Local.FirstOrDefault(x => x.Id == a.Id)
                                  ?? await db.PayrollAdjustments.FirstAsync(x => x.Id == a.Id, ct);
                    tracked.PayrollRunId = payrollRunId;
                }

                foreach (var ab in package.AbsencesToApply)
                {
                    var tracked = db.AbsenceDeductions.Local.FirstOrDefault(x => x.Id == ab.Id)
                                  ?? await db.AbsenceDeductions.FirstAsync(x => x.Id == ab.Id, ct);
                    tracked.AppliedInRunId = payrollRunId;
                }
            }

            await db.SaveChangesAsync(ct);
            if (tx is not null)
                await tx.CommitAsync(ct);
            PayrollRunMetrics.WriteBatches.Add(1);
            return (toSave.Count, already);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException?.Message.Contains("23505", StringComparison.Ordinal) == true
            || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
        {
            if (tx is not null)
                await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var saved = 0;
            var skipped = already;
            foreach (var package in toSave)
            {
                var (ok, exists) = await SaveOnePackageAsync(payrollRunId, runningJobId, package, ct);
                if (ok) saved++;
                else if (exists) skipped++;
            }
            return (saved, skipped);
        }
        catch
        {
            if (tx is not null)
                await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<int> StopRunJobsAsync(Guid payrollRunId, CancellationToken ct)
    {
        // A bulk update, not tracked entities: a worker's checkpoint changes the job's row all the time, and a cancellation must not
        // lose to it on the concurrency token. The update takes the row's lock: it waits for a worker's save in flight (which holds
        // a share lock on the row until its commit), and every later save of a worker waits for this transaction and then finds the
        // job stopped.
        var stopped = await db.PayrollRunJobs
            .Where(j => j.PayrollRunId == payrollRunId
                        && (j.Status == PayrollRunJobStatus.Pending || j.Status == PayrollRunJobStatus.Running
                            || j.Status == PayrollRunJobStatus.Paused || j.Status == PayrollRunJobStatus.Failed))
            .ExecuteUpdateAsync(
                s => s.SetProperty(j => j.Status, PayrollRunJobStatus.Cancelled)
                      .SetProperty(j => j.LeaseOwner, (string?)null)
                      .SetProperty(j => j.LeaseUntil, (DateTime?)null),
                ct);

        await db.PayrollRunPartitions
            .Where(p => p.Status != PayrollRunPartitionStatus.Completed && p.Status != PayrollRunPartitionStatus.Cancelled
                        && db.PayrollRunJobs.Any(j => j.Id == p.PayrollRunJobId && j.PayrollRunId == payrollRunId))
            .ExecuteUpdateAsync(
                s => s.SetProperty(p => p.Status, PayrollRunPartitionStatus.Cancelled)
                      .SetProperty(p => p.WorkerId, (string?)null),
                ct);

        return stopped;
    }

    public async Task<PayrollRunInputsReleased> ReleaseRunInputsAsync(Guid payrollRunId, CancellationToken ct)
    {
        // The recovery deductions a correction run created for its negative lines (PLAN-56 §1), while no run has applied them. They
        // name their line; the lines are still there when a release runs (a caller deletes lines after it, if at all).
        var recoveryReferences = await db.SalaryLines
            .Where(l => l.PayrollRunId == payrollRunId)
            .Select(l => l.Id)
            .ToListAsync(ct);
        var recoveriesDeleted = 0;
        foreach (var chunk in recoveryReferences.Select(CorrectionRecovery.SourceReferenceOf).Chunk(500))
        {
            recoveriesDeleted += await db.DeductionRecords
                .Where(d => d.DeductionType == CorrectionRecovery.DeductionType
                            && d.AppliedInRunId == null
                            && d.SourceReference != null && chunk.Contains(d.SourceReference))
                .ExecuteDeleteAsync(ct);
        }

        // The applied markers, and nothing else of the rows (PLAN-57 adds its own fields to an absence without changing this).
        var deductions = await db.DeductionRecords
            .Where(d => d.AppliedInRunId == payrollRunId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.AppliedInRunId, (Guid?)null), ct);
        var absences = await db.AbsenceDeductions
            .Where(a => a.AppliedInRunId == payrollRunId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.AppliedInRunId, (Guid?)null), ct);
        var adjustments = await db.PayrollAdjustments
            .Where(a => a.PayrollRunId == payrollRunId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.PayrollRunId, (Guid?)null), ct);

        return new PayrollRunInputsReleased(deductions, absences, adjustments, recoveriesDeleted);
    }

    public async Task<(int Lines, int Components)> DeleteRunLinesAsync(Guid payrollRunId, CancellationToken ct)
    {
        var components = await db.SalaryLineComponents
            .Where(c => db.SalaryLines.Any(l => l.Id == c.SalaryLineId && l.PayrollRunId == payrollRunId))
            .ExecuteDeleteAsync(ct);
        var lines = await db.SalaryLines.Where(l => l.PayrollRunId == payrollRunId).ExecuteDeleteAsync(ct);
        return (lines, components);
    }

    public async Task<bool> TryClaimPartitionAsync(Guid partitionId, string workerId, CancellationToken ct)
    {
        var partition = await db.PayrollRunPartitions.FirstOrDefaultAsync(p => p.Id == partitionId, ct);
        if (partition is null) return false;
        if (partition.Status is not (PayrollRunPartitionStatus.Pending or PayrollRunPartitionStatus.Running))
            return false;
        if (partition.Status == PayrollRunPartitionStatus.Running
            && !string.IsNullOrEmpty(partition.WorkerId)
            && !string.Equals(partition.WorkerId, workerId, StringComparison.Ordinal))
            return false;

        partition.Status = PayrollRunPartitionStatus.Running;
        partition.WorkerId = workerId;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
