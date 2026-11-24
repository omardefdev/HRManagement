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
}
