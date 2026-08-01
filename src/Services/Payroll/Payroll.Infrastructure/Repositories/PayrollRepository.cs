using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Payroll.Application.Abstractions;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace Payroll.Infrastructure.Repositories;

public class PayrollRepository(PayrollDbContext db) : IPayrollRepository
{
    public IQueryable<PayrollRun> PayrollRuns => db.PayrollRuns;
    public IQueryable<EmployeePayProfile> EmployeePayProfiles => db.EmployeePayProfiles;
    public IQueryable<EmployeePayProfileHistory> EmployeePayProfileHistories => db.EmployeePayProfileHistories;
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

    public Task<PayrollRun?> GetPayrollRunAsync(Guid id, bool includeLines, CancellationToken ct)
    {
        var q = db.PayrollRuns
            .Include(r => r.Approvals)
            .AsQueryable();
        if (includeLines) q = q.Include(r => r.SalaryLines);
        return q.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public Task<PayrollConfiguration?> GetActiveConfigurationAsync(CancellationToken ct) =>
        db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive
                && c.PublicationStatus == PayrollConfigPublicationStatus.Published, ct);

    public async Task<PayrollConfiguration?> GetPublishedConfigurationForPeriodAsync(
        DateTime periodStart,
        CancellationToken ct)
    {
        var configs = await db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .AsNoTracking()
            .Where(c => c.PublicationStatus == PayrollConfigPublicationStatus.Published)
            .ToListAsync(ct);
        return PayrollConfigurationVersioning.ResolvePublishedForPeriod(configs, periodStart);
    }

    public Task<PayrollConfiguration?> GetConfigurationByIdAsync(Guid id, CancellationToken ct) =>
        db.PayrollConfigurations
            .Include(c => c.TaxBrackets)
            .AsNoTracking()
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

    public async Task<(bool Saved, bool AlreadyExists)> SaveEmployeePackageAtomicAsync(
        Guid payrollRunId,
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

    public async Task<(int Saved, int AlreadyExists)> SaveEmployeePackagesBulkAsync(
        Guid payrollRunId,
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
                var (ok, exists) = await SaveEmployeePackageAtomicAsync(payrollRunId, package, ct);
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
