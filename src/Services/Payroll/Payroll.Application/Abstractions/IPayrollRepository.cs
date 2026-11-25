using Microsoft.EntityFrameworkCore.Storage;
using Payroll.Application.Services;
using Payroll.Domain.Entities;

namespace Payroll.Application.Abstractions;

public interface IPayrollRepository
{
    IQueryable<PayrollRun> PayrollRuns { get; }
    IQueryable<EmployeePayProfile> EmployeePayProfiles { get; }
    IQueryable<EmployeePayProfileHistory> EmployeePayProfileHistories { get; }
    IQueryable<PayProfileChangeRequest> PayProfileChangeRequests { get; }
    IQueryable<EmployeeBankAccount> EmployeeBankAccounts { get; }
    IQueryable<PayrollReferenceChangeRequest> PayrollReferenceChangeRequests { get; }
    IQueryable<SalaryScale> SalaryScales { get; }
    IQueryable<AllowanceRecord> AllowanceRecords { get; }
    IQueryable<CompensationType> CompensationTypes { get; }
    IQueryable<DeductionRecord> DeductionRecords { get; }
    IQueryable<DeductionType> DeductionTypes { get; }
    IQueryable<PayrollAdjustment> PayrollAdjustments { get; }
    IQueryable<PayrollConfiguration> PayrollConfigurations { get; }
    IQueryable<OfficialHoliday> OfficialHolidays { get; }
    IQueryable<AbsenceDeduction> AbsenceDeductions { get; }
    IQueryable<SalaryLine> SalaryLines { get; }
    IQueryable<SalaryLineComponent> SalaryLineComponents { get; }
    IQueryable<EndOfServiceSettlement> EndOfServiceSettlements { get; }
    IQueryable<PayslipDelivery> PayslipDeliveries { get; }
    IQueryable<PayGrade> PayGrades { get; }
    IQueryable<PayrollAuditRecord> PayrollAuditRecords { get; }
    IQueryable<PayrollRunJob> PayrollRunJobs { get; }
    IQueryable<PayrollRunPartition> PayrollRunPartitions { get; }
    IQueryable<PayrollRunInputSnapshot> PayrollRunInputSnapshots { get; }
    IQueryable<PaymentBatch> PaymentBatches { get; }
    IQueryable<PaymentOrder> PaymentOrders { get; }
    IQueryable<FundingDecision> FundingDecisions { get; }
    IQueryable<DisbursementPriorityRule> DisbursementPriorityRules { get; }
    IQueryable<FraudAlert> FraudAlerts { get; }
    IQueryable<PaymentHold> PaymentHolds { get; }
    IQueryable<LegalDecisionRecord> LegalDecisionRecords { get; }
    IQueryable<LegalAttestation> LegalAttestations { get; }
    IQueryable<PayrollRunRevision> PayrollRunRevisions { get; }
    IQueryable<PayrollReversalRequest> PayrollReversalRequests { get; }
    IQueryable<PayrollRunApproval> PayrollRunApprovals { get; }

    Task<PayrollRun?> GetPayrollRunAsync(Guid id, bool includeLines, CancellationToken ct);
    /// <summary>The version that governs today (the payroll calendar's date); see <c>PayrollConfigurationVersioning.ResolveGoverning</c>.</summary>
    Task<PayrollConfiguration?> GetActiveConfigurationAsync(CancellationToken ct);

    /// <summary>The version of a regime (or of a code) that governs today.</summary>
    Task<PayrollConfiguration?> GetConfigurationByRegimeCodeAsync(string regimeCode, CancellationToken ct);

    /// <summary>The version a run of the period starting on <paramref name="periodStart"/> is pinned to.</summary>
    Task<PayrollConfiguration?> GetPublishedConfigurationForPeriodAsync(DateTime periodStart, CancellationToken ct);

    /// <summary>A version with every table that governs pay (not tracked): brackets, statutory deductions, holidays, priority rules, field sources.</summary>
    Task<PayrollConfiguration?> GetConfigurationByIdAsync(Guid id, CancellationToken ct);

    /// <summary>PLAN-54: a version with all of its tables and its editors, tracked for a change of the cycle or of a draft.</summary>
    Task<PayrollConfiguration?> GetFrameworkForUpdateAsync(Guid id, CancellationToken ct);
    Task<EmployeePayProfile?> GetActiveProfileAsync(Guid employeeId, CancellationToken ct);
    Task<PayrollRunJob?> GetActiveJobAsync(Guid payrollRunId, CancellationToken ct);
    Task AddAsync<T>(T entity, CancellationToken ct) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<int> SaveChangesAsync(CancellationToken ct);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct);
    void ClearChangeTracker();
    void MarkAsAdded<T>(T entity) where T : class;

    Task<(bool Saved, bool AlreadyExists)> SaveEmployeePackageAtomicAsync(
        Guid payrollRunId,
        PayrollEmployeeCalculationPackage package,
        CancellationToken ct);

    /// <summary>PERF-001: persist multiple employee packages in one transaction.</summary>
    Task<(int Saved, int AlreadyExists)> SaveEmployeePackagesBulkAsync(
        Guid payrollRunId,
        IReadOnlyList<PayrollEmployeeCalculationPackage> packages,
        CancellationToken ct);

    /// <summary>PERF-001: atomic claim Pending→Running for a partition.</summary>
    Task<bool> TryClaimPartitionAsync(Guid partitionId, string workerId, CancellationToken ct);

    /// <summary>
    /// PLAN-56 (G-PAY-04): the calculation worker's save. In the same transaction as the save it checks that
    /// <paramref name="jobId"/> is still running, holding the job's row against a cancellation until the commit; a job that was
    /// cancelled, paused or failed gets nothing written (null): no line, no component, no input marked applied. So a package
    /// written after a cancellation released the run's inputs can neither re-attach an input nor leave a line.
    /// </summary>
    Task<(int Saved, int AlreadyExists)?> SaveEmployeePackagesForJobAsync(
        Guid payrollRunId,
        Guid jobId,
        IReadOnlyList<PayrollEmployeeCalculationPackage> packages,
        CancellationToken ct);

    /// <summary>
    /// PLAN-56 (G-PAY-04): stops a run's jobs that are not finished (pending, running, paused, failed) and their open partitions, in
    /// the caller's transaction. It waits for a worker's save in flight and then holds the jobs' rows until the caller commits, so the
    /// caller reads every input the workers attached. Returns how many jobs it stopped.
    /// </summary>
    Task<int> StopRunJobsAsync(Guid payrollRunId, CancellationToken ct);

    /// <summary>
    /// PLAN-56 (review): takes a job for a calculation that goes on (a resume, or a start over a job left behind), by one conditional
    /// statement: only a job that is paused, failed, pending, or running with no live lease becomes running. False when the job is
    /// anything else by now: a cancellation or a restart that committed first is never undone by a resume that read the job before
    /// it. Called inside the caller's transaction, it holds the job's row until the commit, so a cancellation waits and then stops
    /// the job the ordinary way.
    /// </summary>
    Task<bool> ClaimJobForCalculationAsync(Guid jobId, CancellationToken ct);

    /// <summary>PLAN-56 (review): pauses a job only while it is running (one conditional statement). False otherwise.</summary>
    Task<bool> PauseRunningJobAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// PLAN-56 (review): records a partition's failure: the partition fails unless it was completed or cancelled, and the job fails
    /// only while it is running. A cancelled or paused job keeps its state: the failure of a worker never revives or relabels it.
    /// </summary>
    Task FailRunningJobAsync(Guid jobId, Guid partitionId, string error, CancellationToken ct);

    /// <summary>
    /// PLAN-56 (G-PAY-07): frees what a run's calculation consumed, in the caller's transaction: the applied marker of its
    /// deductions, absences and adjustments, and nothing else of them. It also deletes the recovery deductions a correction run
    /// created for its lines that no run has applied. Idempotent: a second call finds nothing.
    /// </summary>
    Task<PayrollRunInputsReleased> ReleaseRunInputsAsync(Guid payrollRunId, CancellationToken ct);

    /// <summary>PLAN-56 (G-PAY-04): deletes a run's lines and their components, in the caller's transaction.</summary>
    Task<(int Lines, int Components)> DeleteRunLinesAsync(Guid payrollRunId, CancellationToken ct);
}

/// <summary>PLAN-56: what a release of a run's inputs freed.</summary>
public sealed record PayrollRunInputsReleased(int Deductions, int Absences, int Adjustments, int RecoveriesDeleted)
{
    public int Total => Deductions + Absences + Adjustments + RecoveriesDeleted;
}
