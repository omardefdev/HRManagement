using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
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
    public DbSet<OfficialHoliday> OfficialHolidays => Set<OfficialHoliday>();
    public DbSet<LegalDecisionRecord> LegalDecisionRecords => Set<LegalDecisionRecord>();
    public DbSet<LegalAttestation> LegalAttestations => Set<LegalAttestation>();
    public DbSet<PayrollConfigChangeNotice> PayrollConfigChangeNotices => Set<PayrollConfigChangeNotice>();
    public DbSet<EmployeePayProfileHistory> EmployeePayProfileHistories => Set<EmployeePayProfileHistory>();
    public DbSet<PayrollRunApproval> PayrollRunApprovals => Set<PayrollRunApproval>();
    public DbSet<PayslipDelivery> PayslipDeliveries => Set<PayslipDelivery>();
    public DbSet<AbsenceDeduction> AbsenceDeductions => Set<AbsenceDeduction>();
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

            // DATA-001 / PAY-003: one live run per tenant/period/type (Reversed=5, Cancelled=8 excluded)
            entity.HasIndex(e => new { e.TenantId, e.PeriodStart, e.PeriodEnd, e.RunType })
                .IsUnique()
                .HasDatabaseName("IX_PayrollRuns_LivePeriod")
                .HasFilter("\"Status\" <> 5 AND \"Status\" <> 8");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<SalaryLine>(entity =>
        {
            entity.HasIndex(e => new { e.PayrollRunId, e.EmployeeId })
                .IsUnique()
                .HasDatabaseName("IX_SalaryLines_Run_Employee");
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
            entity.HasIndex(e => new { e.TenantId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.PayrollRunId, e.EmployeeId });
            entity.HasIndex(e => e.PaymentBatchId);
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
        });

        modelBuilder.Entity<DeductionRecord>(entity =>
        {
            entity.HasOne(e => e.AppliedInRun)
                  .WithMany()
                  .HasForeignKey(e => e.AppliedInRunId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AbsenceDeduction>(entity =>
        {
            // PAY-DD-001: one absence deduction per upstream fact. Without this a redelivered
            // leave approval silently charged the employee twice.
            entity.HasIndex(e => e.SourceReference)
                  .IsUnique()
                  .HasFilter("\"SourceReference\" IS NOT NULL");
        });

        modelBuilder.Entity<PayrollAdjustment>(entity =>
        {
            entity.HasOne(e => e.PayrollRun)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollRunId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SalaryScale>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasMany(e => e.PayGrades)
                  .WithOne(g => g.SalaryScale)
                  .HasForeignKey(g => g.SalaryScaleId)
                  .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasIndex(e => e.EmployeeId)
                .IsUnique()
                .HasDatabaseName("IX_EmployeePayProfiles_OneActive")
                .HasFilter("\"IsActive\" = TRUE");
            entity.HasOne(e => e.PayGrade)
                  .WithMany()
                  .HasForeignKey(e => e.PayGradeId)
                  .OnDelete(DeleteBehavior.SetNull);
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
        });

        modelBuilder.Entity<OfficialHoliday>(entity =>
        {
            entity.HasOne(e => e.PayrollConfiguration)
                  .WithMany()
                  .HasForeignKey(e => e.PayrollConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LegalDecisionRecord>(entity =>
        {
            entity.HasIndex(e => e.DecisionId).IsUnique();
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
