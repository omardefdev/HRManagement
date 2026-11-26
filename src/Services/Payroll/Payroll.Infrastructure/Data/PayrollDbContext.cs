using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;

namespace Payroll.Infrastructure.Data;

public class PayrollDbContext : TenantAwareDbContext
{
    public PayrollDbContext(DbContextOptions<PayrollDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<SalaryLine> SalaryLines => Set<SalaryLine>();
    public DbSet<CompensationType> CompensationTypes => Set<CompensationType>();
    public DbSet<AllowanceRecord> AllowanceRecords => Set<AllowanceRecord>();
    public DbSet<DeductionRecord> DeductionRecords => Set<DeductionRecord>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<PayrollAuditRecord> PayrollAuditRecords => Set<PayrollAuditRecord>();
    public DbSet<PaymentBatch> PaymentBatches => Set<PaymentBatch>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();
    public DbSet<PaymentBatchFile> PaymentBatchFiles => Set<PaymentBatchFile>();
    public DbSet<BankStatementLine> BankStatementLines => Set<BankStatementLine>();
    public DbSet<FundingDecision> FundingDecisions => Set<FundingDecision>();
    public DbSet<DisbursementPriorityRule> DisbursementPriorityRules => Set<DisbursementPriorityRule>();
    public DbSet<ArrearsLedgerEntry> ArrearsLedgerEntries => Set<ArrearsLedgerEntry>();
    public DbSet<PayrollRunJob> PayrollRunJobs => Set<PayrollRunJob>();
    public DbSet<PayrollRunPartition> PayrollRunPartitions => Set<PayrollRunPartition>();
    public DbSet<PayrollRunInputSnapshot> PayrollRunInputSnapshots => Set<PayrollRunInputSnapshot>();
    public DbSet<SalaryScale> SalaryScales => Set<SalaryScale>();
    public DbSet<PayGrade> PayGrades => Set<PayGrade>();
    public DbSet<DeductionType> DeductionTypes => Set<DeductionType>();
    public DbSet<EmployeePayProfile> EmployeePayProfiles => Set<EmployeePayProfile>();
    public DbSet<SalaryLineComponent> SalaryLineComponents => Set<SalaryLineComponent>();
    public DbSet<EndOfServiceSettlement> EndOfServiceSettlements => Set<EndOfServiceSettlement>();
    public DbSet<PayrollConfiguration> PayrollConfigurations => Set<PayrollConfiguration>();
    public DbSet<TaxBracket> TaxBrackets => Set<TaxBracket>();
    public DbSet<StatutoryDeductionRule> StatutoryDeductionRules => Set<StatutoryDeductionRule>();
    public DbSet<PayrollConfigurationFieldSource> PayrollConfigurationFieldSources => Set<PayrollConfigurationFieldSource>();
    public DbSet<PayrollConfigurationEditor> PayrollConfigurationEditors => Set<PayrollConfigurationEditor>();
    public DbSet<OfficialHoliday> OfficialHolidays => Set<OfficialHoliday>();
    public DbSet<LegalDecisionRecord> LegalDecisionRecords => Set<LegalDecisionRecord>();
    public DbSet<LegalAttestation> LegalAttestations => Set<LegalAttestation>();
    public DbSet<PayrollConfigChangeNotice> PayrollConfigChangeNotices => Set<PayrollConfigChangeNotice>();
    public DbSet<EmployeePayProfileHistory> EmployeePayProfileHistories => Set<EmployeePayProfileHistory>();
    public DbSet<PayProfileChangeRequest> PayProfileChangeRequests => Set<PayProfileChangeRequest>();
    public DbSet<EmployeeBankAccount> EmployeeBankAccounts => Set<EmployeeBankAccount>();
    public DbSet<PayrollReferenceChangeRequest> PayrollReferenceChangeRequests => Set<PayrollReferenceChangeRequest>();
    public DbSet<PayrollRunApproval> PayrollRunApprovals => Set<PayrollRunApproval>();
    public DbSet<PayrollRunRevision> PayrollRunRevisions => Set<PayrollRunRevision>();
    public DbSet<PayrollReversalRequest> PayrollReversalRequests => Set<PayrollReversalRequest>();
    public DbSet<PayslipDelivery> PayslipDeliveries => Set<PayslipDelivery>();
    public DbSet<AbsenceDeduction> AbsenceDeductions => Set<AbsenceDeduction>();
    public DbSet<AttendanceSummaryReceipt> AttendanceSummaryReceipts => Set<AttendanceSummaryReceipt>();
    public DbSet<FraudAlert> FraudAlerts => Set<FraudAlert>();
    public DbSet<PaymentHold> PaymentHolds => Set<PaymentHold>();
    public DbSet<ProofOfLifeRecord> ProofOfLifeRecords => Set<ProofOfLifeRecord>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<HRManagement.SharedKernel.Entities.ExportJob> ExportJobs =>
        Set<HRManagement.SharedKernel.Entities.ExportJob>();
    public DbSet<HRManagement.SharedKernel.Entities.OrgUnitSnapshot> OrgUnitSnapshots =>
        Set<HRManagement.SharedKernel.Entities.OrgUnitSnapshot>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();
    public DbSet<HRManagement.SharedKernel.Entities.IntegrationMessageReceipt> IntegrationMessageReceipts =>
        Set<HRManagement.SharedKernel.Entities.IntegrationMessageReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
            .SelectMany(t => t.GetProperties())
            .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
        {
            property.SetPrecision(18);
            property.SetScale(2);
        }

        modelBuilder.Entity<PayrollRun>(entity =>
        {
            entity.Property(e => e.RunType).HasConversion<string>().HasMaxLength(50);
            // PLAN-56 (G-PAY-04): every run that existed before the column is its first version.
            entity.Property(e => e.Revision).HasDefaultValue(1);
            entity.Property(e => e.ConfigContentHash).HasMaxLength(64);
            entity.HasOne(e => e.PayrollConfiguration)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.SalaryLines)
                  .WithOne(e => e.PayrollRun)
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.AuditRecords)
                  .WithOne(e => e.PayrollRun)
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.PaymentBatches)
                  .WithOne(e => e.PayrollRun)
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Approvals)
                  .WithOne(e => e.PayrollRun)
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Cascade);

            // DATA-001 / PAY-003: one live run per tenant/period/type (Reversed=5, Cancelled=8 excluded).
            // PLAN-56 (§2 rule 4): limited to regular runs. A correction has the period of its source, so the old key refused the
            // second correction of a month for ever; and a final settlement is per employee (its key is PLAN-64's).
            entity.HasIndex(e => new { e.TenantId, e.PeriodStart, e.PeriodEnd, e.RunType })
                .IsUnique()
                .HasDatabaseName(PayrollRunIndexNames.LivePeriod)
                .HasFilter("\"RunType\" = 'Regular' AND \"Status\" <> 5 AND \"Status\" <> 8");

            // PLAN-56 (§2 rule 4): one correction not yet posted per source run (Posted=2, Reversed=5, Cancelled=8 are not open).
            entity.HasIndex(e => new { e.TenantId, e.SourceRunId })
                .IsUnique()
                .HasDatabaseName(PayrollRunIndexNames.OneOpenCorrectionPerSource)
                .HasFilter("\"RunType\" = 'Correction' AND \"SourceRunId\" IS NOT NULL AND \"Status\" NOT IN (2, 5, 8)");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<SalaryLine>(entity =>
        {
            entity.HasIndex(e => new { e.PayrollRunId, e.EmployeeId })
                .IsUnique()
                .HasDatabaseName("IX_SalaryLines_Run_Employee");
            // PLAN-55 (D-PAY-15): the account the run pinned. An account a line names is never deleted.
            entity.HasOne(e => e.BankAccount)
                  .WithMany()
                  .HasForeignKey(e => e.BankAccountId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmployeeBankAccount>(entity =>
        {
            // PLAN-55 (§1): one Active (1) and one PendingVerification (0) account per employee and agency. The status is part of the
            // index so that EF orders the two updates of a verification: the account in force leaves the index before its replacement
            // takes its place.
            entity.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Status })
                .IsUnique()
                .HasDatabaseName(PayrollChangeIndexNames.OneActiveOnePendingBankAccount)
                .HasFilter("\"Status\" IN (0, 1)");
            entity.HasIndex(e => e.EmployeeId);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<PaymentBatch>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.BatchKind).HasConversion<string>().HasMaxLength(50);
            entity.HasIndex(e => new { e.PayrollRunId, e.BankTransferReference })
                .IsUnique()
                .HasDatabaseName("IX_PaymentBatches_Run_BankRef")
                .HasFilter("\"BankTransferReference\" IS NOT NULL");
            entity.HasOne(e => e.FundingDecision)
                  .WithMany()
                  .HasForeignKey(e => e.FundingDecisionId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.FundingDecisionId)
                .HasDatabaseName("IX_PaymentBatches_FundingDecisionId");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<PaymentOrder>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.HoldReason).HasMaxLength(PaymentOrderHoldReasons.MaxLength);
            entity.HasIndex(e => e.PaymentBatchFileId);
            entity.HasIndex(e => new { e.TenantId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.PayrollRunId, e.EmployeeId });
            entity.HasIndex(e => e.PaymentBatchId);
            entity.ConfigureOptimisticConcurrency(this);
        });

        // PLAN-58 (§2): one file per sending of a batch, stored and never regenerated. The unique sequence lets one of two concurrent
        // sendings in; a new table's enum is stored as an integer (PLAN-57's rule).
        modelBuilder.Entity<PaymentBatchFile>(entity =>
        {
            entity.HasIndex(e => new { e.PaymentBatchId, e.Sequence })
                .IsUnique()
                .HasDatabaseName(PayrollDisbursementIndexNames.OneFilePerSequence);
            entity.HasOne<PaymentBatch>()
                .WithMany()
                .HasForeignKey(e => e.PaymentBatchId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Sha256).HasMaxLength(64);
            entity.Property(e => e.ReissueReason).HasMaxLength(PaymentBatchFile.ReasonMaxLength);
            entity.Property(e => e.ControlTotal).HasPrecision(18, 2);
            entity.Ignore(e => e.FileName);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<BankStatementLine>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.StatementReference }).IsUnique();
            entity.HasIndex(e => e.MatchedPaymentOrderId);
        });

        modelBuilder.Entity<FundingDecision>(entity =>
        {
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.HasOne(e => e.PayrollRun)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.PayrollRunId)
                .HasDatabaseName("IX_FundingDecisions_PayrollRunId");
            entity.HasIndex(e => e.PriorityRuleSetId)
                .HasDatabaseName("IX_FundingDecisions_PriorityRuleSetId");
        });

        modelBuilder.Entity<DisbursementPriorityRule>(entity =>
        {
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.LegalSource).HasMaxLength(512);
            entity.HasIndex(e => new { e.RuleSetId, e.Priority })
                .HasDatabaseName("IX_DisbursementPriorityRules_RuleSet_Priority");
            entity.HasIndex(e => new { e.RuleSetId, e.Category })
                .HasDatabaseName("IX_DisbursementPriorityRules_RuleSet_Category");
            // PLAN-54: a rule inside a framework version; null for a general rule older than PLAN-54.
            entity.Property(e => e.LegalStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.InterimExceptionId).HasMaxLength(LegalValueRules.InterimExceptionIdMaxLength);
            entity.HasOne(e => e.PayrollConfiguration)
                  .WithMany(c => c.PriorityRules)
                  .HasForeignKey(e => e.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ArrearsLedgerEntry>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Reason).HasMaxLength(500);
            entity.Property(e => e.CurrencyEra).HasMaxLength(32);
            entity.HasIndex(e => e.EmployeeId)
                .HasDatabaseName("IX_ArrearsLedgerEntries_EmployeeId");
            entity.HasIndex(e => new { e.EmployeeId, e.Status })
                .HasDatabaseName("IX_ArrearsLedgerEntries_Employee_Status");
            entity.HasIndex(e => e.PayrollRunId)
                .HasDatabaseName("IX_ArrearsLedgerEntries_PayrollRunId");
            entity.HasIndex(e => e.SettledByBatchId)
                .HasDatabaseName("IX_ArrearsLedgerEntries_SettledByBatchId");
        });

        modelBuilder.Entity<PayrollRunJob>(entity =>
        {
            entity.Property(e => e.Phase).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.LeaseOwner).HasMaxLength(100);
            entity.Property(e => e.Error).HasMaxLength(2000);
            entity.HasOne(e => e.PayrollRun)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.PayrollRunId)
                .IsUnique()
                .HasDatabaseName("IX_PayrollRunJobs_ActivePerRun")
                .HasFilter("\"Status\" IN ('Pending', 'Running', 'Paused')");
            entity.HasMany(e => e.Partitions)
                  .WithOne(p => p.Job)
                  .HasForeignKey(p => p.PayrollRunJobId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.InputSnapshots)
                  .WithOne(s => s.Job)
                  .HasForeignKey(s => s.PayrollRunJobId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PayrollRunPartition>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.WorkerId).HasMaxLength(100);
            entity.HasIndex(e => new { e.PayrollRunJobId, e.PartitionIndex }).IsUnique();
        });

        modelBuilder.Entity<PayrollRunInputSnapshot>(entity =>
        {
            entity.Property(e => e.ProfileVersion).HasMaxLength(128);
            entity.Property(e => e.InputFingerprint).HasMaxLength(64);
            entity.Property(e => e.ConfigContentHash).HasMaxLength(64);
            entity.HasIndex(e => new { e.PayrollRunJobId, e.EmployeeId }).IsUnique();
            entity.HasIndex(e => new { e.PayrollRunJobId, e.SortOrder });
            // PLAN-55 (D-PAY-15): the account in force at the snapshot.
            entity.HasOne(e => e.BankAccount)
                  .WithMany()
                  .HasForeignKey(e => e.BankAccountId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CompensationType>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<AllowanceRecord>(entity =>
        {
            entity.HasOne(e => e.AllowanceType)
                  .WithMany()
                  .HasForeignKey(e => e.AllowanceTypeId)
                  .OnDelete(DeleteBehavior.Restrict);
            // PLAN-54 (D-PAY-6): the column defaults (Approved, System) are the migration's; EF always writes the value.
            entity.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.TenantId, e.ApprovalStatus })
                  .HasDatabaseName("IX_AllowanceRecords_Tenant_ApprovalStatus");
        });

        modelBuilder.Entity<DeductionRecord>(entity =>
        {
            entity.HasOne(e => e.AppliedInRun)
                  .WithMany()
                  .HasForeignKey(e => e.AppliedInRunId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.TenantId, e.ApprovalStatus })
                  .HasDatabaseName("IX_DeductionRecords_Tenant_ApprovalStatus");
        });

        modelBuilder.Entity<AbsenceDeduction>(entity =>
        {
            // PAY-DD-001: one absence deduction per upstream fact. Without this a redelivered
            // leave approval silently charged the employee twice.
            entity.HasIndex(e => e.SourceReference)
                  .IsUnique()
                  .HasFilter("\"SourceReference\" IS NOT NULL");
            // PLAN-57 (§5): the carried differences of an employee that no run has applied yet.
            entity.HasIndex(e => new { e.EmployeeId, e.CarryForward, e.AppliedInRunId });
            // PLAN-57 (review): a carried difference is free for every later regular run. Two runs that calculate at once would both
            // mark it applied; with the row version the second to save is refused instead.
            entity.ConfigureOptimisticConcurrency(this);
        });

        // PLAN-57 (§4 rule 6): one receipt per attendance summary. The consumer locks its row, and xmin guards any other writer.
        modelBuilder.Entity<AttendanceSummaryReceipt>(entity =>
        {
            entity.HasIndex(e => e.SummaryId).IsUnique();
            entity.HasIndex(e => new { e.EmployeeId, e.PeriodStart, e.PeriodEnd });
            entity.HasIndex(e => new { e.TenantId, e.PeriodStart });
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<PayrollAdjustment>(entity =>
        {
            entity.HasOne(e => e.PayrollRun)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.ApprovalStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(e => new { e.TenantId, e.ApprovalStatus })
                  .HasDatabaseName("IX_PayrollAdjustments_Tenant_ApprovalStatus");
            // PLAN-56 (G-PAY-03): an adjustment aimed at the correction of one posted run. That run is never deleted from under it.
            entity.HasOne(e => e.TargetSourceRun)
                  .WithMany()
                  .HasForeignKey(e => e.TargetSourceRunId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.TargetSourceRunId)
                  .HasDatabaseName("IX_PayrollAdjustments_TargetSourceRunId");
        });

        modelBuilder.Entity<SalaryScale>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            // PLAN-53 (§4): a scale with grades is not deleted (was Cascade: deleting a scale deleted its grades).
            entity.HasMany(e => e.PayGrades)
                  .WithOne(g => g.SalaryScale)
                  .HasForeignKey(g => g.SalaryScaleId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayGrade>(entity =>
        {
            entity.HasIndex(e => new { e.SalaryScaleId, e.GradeCode }).IsUnique();
        });

        modelBuilder.Entity<DeductionType>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<EmployeePayProfile>(entity =>
        {
            entity.Property(e => e.RegimeCode).HasMaxLength(50);
            entity.HasIndex(e => e.EmployeeId)
                .IsUnique()
                .HasDatabaseName("IX_EmployeePayProfiles_OneActive")
                .HasFilter("\"IsActive\" = TRUE");
            // PLAN-53 (§4): a grade a pay profile names is not deleted (was SetNull: the delete emptied the grade of every profile).
            entity.HasOne(e => e.PayGrade)
                  .WithMany()
                  .HasForeignKey(e => e.PayGradeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<PayProfileChangeRequest>(entity =>
        {
            // PLAN-53 (D-53-3): one open request per employee and agency (PendingApproval = 0, Scheduled = 1).
            entity.HasIndex(e => new { e.TenantId, e.EmployeeId })
                .IsUnique()
                .HasDatabaseName(PayrollChangeIndexNames.OneOpenPayProfileChange)
                .HasFilter("\"State\" IN (0, 1)");
            // The activation job: the scheduled requests that are due.
            entity.HasIndex(e => new { e.State, e.EffectiveFrom });
            // The branch scope of lists.
            entity.HasIndex(e => e.OrgUnitId);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<PayrollReferenceChangeRequest>(entity =>
        {
            entity.Property(e => e.ProposedJson).HasColumnType("text");
            entity.Property(e => e.CurrentJson).HasColumnType("text");
            // One open request per record (PendingApproval = 0); a creation has no record yet.
            entity.HasIndex(e => new { e.TenantId, e.EntityType, e.EntityId })
                .IsUnique()
                .HasDatabaseName(PayrollChangeIndexNames.OneOpenReferenceChange)
                .HasFilter("\"State\" = 0 AND \"EntityId\" IS NOT NULL");
            entity.HasIndex(e => e.State);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<SalaryLineComponent>(entity =>
        {
            entity.HasOne(e => e.SalaryLine)
                  .WithMany()
                  .HasForeignKey(e => e.SalaryLineId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.ComponentType)
                  .HasConversion<string>()
                  .HasMaxLength(50);
        });

        modelBuilder.Entity<EndOfServiceSettlement>(entity =>
        {
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => e.SeparationRecordId)
                  .IsUnique()
                  .HasFilter("\"SeparationRecordId\" IS NOT NULL");
        });

        modelBuilder.Entity<PayrollConfiguration>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Code, e.VersionNumber })
                .IsUnique()
                .HasDatabaseName("IX_PayrollConfigurations_Tenant_Code_Version");
            entity.HasIndex(e => new { e.TenantId, e.IsActive })
                .HasDatabaseName("IX_PayrollConfigurations_Tenant_Active")
                .HasFilter("\"IsActive\" = TRUE");
            entity.HasIndex(e => e.RegimeCode);
            entity.Property(e => e.RegimeCode).HasMaxLength(50);
            entity.Property(e => e.NameAr).HasMaxLength(200);
            entity.Property(e => e.NonWorkingDays).HasMaxLength(100);
            entity.Property(e => e.TaxableBaseMode).HasMaxLength(50);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.CurrencyCode).HasMaxLength(3);
            entity.Property(e => e.ProrationDaysBasis).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.PublicationStatus).HasConversion<string>().HasMaxLength(50);
            entity.Property(e => e.RoundingMode).HasConversion<string>().HasMaxLength(50);
            entity.HasOne<PayrollConfiguration>()
                  .WithMany()
                  .HasForeignKey(e => e.SupersedesConfigurationId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.TaxBrackets)
                  .WithOne(t => t.PayrollConfiguration)
                  .HasForeignKey(t => t.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
            // PLAN-54: the version owns its statutory deductions, field sources and editors (its holidays and priority rules are
            // mapped on their own entities).
            entity.HasMany(e => e.StatutoryDeductions)
                  .WithOne(d => d.PayrollConfiguration)
                  .HasForeignKey(d => d.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.FieldSources)
                  .WithOne(f => f.PayrollConfiguration)
                  .HasForeignKey(f => f.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Editors)
                  .WithOne(x => x.PayrollConfiguration)
                  .HasForeignKey(x => x.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            // PLAN-54 (§3): an approval fixes the content it fingerprinted. Every write of a version's content also writes the
            // version's own row (UpdatedAt), so an edit that lands between an approver's read and his save, or two publications
            // that supersede the same version, fail on this token instead of leaving a fingerprint of other content.
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<TaxBracket>(entity =>
        {
            entity.Property(e => e.LegalStatus).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<StatutoryDeductionRule>(entity =>
        {
            entity.HasIndex(e => new { e.PayrollConfigurationId, e.Code })
                  .IsUnique()
                  .HasDatabaseName("IX_StatutoryDeductionRules_Configuration_Code");
            entity.Property(e => e.BaseKind).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.LegalStatus).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<PayrollConfigurationFieldSource>(entity =>
        {
            entity.HasIndex(e => new { e.PayrollConfigurationId, e.FieldName })
                  .IsUnique()
                  .HasDatabaseName("IX_PayrollConfigurationFieldSources_Configuration_Field");
            entity.Property(e => e.LegalStatus).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<PayrollConfigurationEditor>(entity =>
        {
            entity.HasIndex(e => new { e.PayrollConfigurationId, e.UserId })
                  .HasDatabaseName("IX_PayrollConfigurationEditors_Configuration_User");
            entity.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<OfficialHoliday>(entity =>
        {
            entity.HasOne(e => e.PayrollConfiguration)
                  .WithMany(c => c.Holidays)
                  .HasForeignKey(e => e.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.LegalStatus).HasConversion<string>().HasMaxLength(20);
        });

        modelBuilder.Entity<LegalDecisionRecord>(entity =>
        {
            // PLAN-54: a decision id is unique inside an agency (it was unique across agencies, so a second agency's import of the
            // same legal pack would have been refused by the index).
            entity.HasIndex(e => new { e.TenantId, e.DecisionId })
                  .IsUnique()
                  .HasDatabaseName("IX_LegalDecisionRecords_Tenant_DecisionId");
            entity.Property(e => e.DecisionId).HasMaxLength(64);
            entity.Property(e => e.ApprovalStatus).HasMaxLength(32);
            entity.Property(e => e.OfficialSource).HasMaxLength(512);
            entity.Property(e => e.ConfigContentHash).HasMaxLength(64);
        });

        modelBuilder.Entity<LegalAttestation>(entity =>
        {
            entity.HasIndex(e => new { e.PayrollConfigurationId, e.ContentHash });
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(32);
            entity.Property(e => e.DecisionIdsJson).HasColumnType("text");
            entity.Property(e => e.InterimExceptionIdsJson).HasColumnType("text");
            entity.HasOne(e => e.PayrollConfiguration)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PayrollConfigChangeNotice>(entity =>
        {
            entity.HasIndex(e => e.PayrollConfigurationId);
            entity.Property(e => e.ContentHash).HasMaxLength(64);
            entity.Property(e => e.ImpactSummary).HasMaxLength(2000);
            entity.Property(e => e.Channel).HasMaxLength(256);
            entity.HasOne(e => e.PayrollConfiguration)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PayrollRunApproval>(entity =>
        {
            entity.Property(e => e.Decision).HasConversion<string>().HasMaxLength(50);
        });

        // PLAN-56 (G-PAY-04): what each rejected version of a run was. Kept when the rejection deletes the version's lines.
        modelBuilder.Entity<PayrollRunRevision>(entity =>
        {
            entity.HasOne(e => e.PayrollRun)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.PayrollRunId, e.Revision })
                  .IsUnique()
                  .HasDatabaseName(PayrollRunIndexNames.OneRevisionPerVersion);
        });

        // PLAN-56 (G-PAY-06): a reversal is a request another person decides; one request waits on a run at a time.
        modelBuilder.Entity<PayrollReversalRequest>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(e => e.PayrollRun)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.PayrollRunId })
                  .IsUnique()
                  .HasDatabaseName(PayrollRunIndexNames.OnePendingReversalRequest)
                  .HasFilter("\"Status\" = 'Pending'");
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<PayslipDelivery>(entity =>
        {
            entity.HasIndex(e => e.SalaryLineId);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50);
            entity.HasOne(e => e.SalaryLine)
                  .WithMany()
                  .HasForeignKey(e => e.SalaryLineId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeePayProfileHistory>(entity =>
        {
            entity.HasIndex(e => e.EmployeeId);
        });

        modelBuilder.Entity<FraudAlert>(entity =>
        {
            entity.Property(e => e.RuleId).HasMaxLength(16);
            entity.Property(e => e.Category).HasMaxLength(128);
            entity.Property(e => e.Severity).HasMaxLength(32);
            entity.Property(e => e.Status).HasMaxLength(32);
            entity.Property(e => e.Details).HasMaxLength(2000);
            entity.Property(e => e.ResolutionReason).HasMaxLength(2000);
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_FraudAlerts_Status");
            entity.HasIndex(e => e.EmployeeId).HasDatabaseName("IX_FraudAlerts_EmployeeId");
            entity.HasIndex(e => e.PayrollRunId).HasDatabaseName("IX_FraudAlerts_PayrollRunId");
            entity.HasIndex(e => new { e.TenantId, e.Status, e.DetectedAtUtc })
                .HasDatabaseName("IX_FraudAlerts_Tenant_Status_Detected");
        });

        modelBuilder.Entity<PaymentHold>(entity =>
        {
            entity.Property(e => e.Reason).HasMaxLength(2000);
            entity.Property(e => e.HoldType).HasMaxLength(32);
            entity.Property(e => e.Status).HasMaxLength(32);
            entity.Property(e => e.ReleaseReason).HasMaxLength(2000);
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_PaymentHolds_Status");
            entity.HasIndex(e => e.EmployeeId).HasDatabaseName("IX_PaymentHolds_EmployeeId");
            entity.HasIndex(e => e.FraudAlertId).HasDatabaseName("IX_PaymentHolds_FraudAlertId");
            entity.HasIndex(e => new { e.TenantId, e.Status, e.HeldAtUtc })
                .HasDatabaseName("IX_PaymentHolds_Tenant_Status_Held");
        });

        modelBuilder.Entity<ProofOfLifeRecord>(entity =>
        {
            entity.Property(e => e.Method).HasMaxLength(64);
            entity.Property(e => e.Status).HasMaxLength(32);
            entity.Property(e => e.ExceptionReason).HasMaxLength(2000);
            entity.HasIndex(e => e.EmployeeId).HasDatabaseName("IX_ProofOfLifeRecords_EmployeeId");
            entity.HasIndex(e => e.Status).HasDatabaseName("IX_ProofOfLifeRecords_Status");
            entity.HasIndex(e => e.ExpiryDate).HasDatabaseName("IX_ProofOfLifeRecords_ExpiryDate");
        });

        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureIdempotencyRecords();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
