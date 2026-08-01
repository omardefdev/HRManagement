using HR.Contracts.Domain.Entities;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Infrastructure.Data;

public class ContractsDbContext : TenantAwareDbContext
{
    public ContractsDbContext(DbContextOptions<ContractsDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext)
    {
    }

    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractType> ContractTypes => Set<ContractType>();
    public DbSet<ContractSignature> ContractSignatures => Set<ContractSignature>();
    public DbSet<ProbationRecord> ProbationRecords => Set<ProbationRecord>();
    public DbSet<SeparationRecord> SeparationRecords => Set<SeparationRecord>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<AdvanceRequest> AdvanceRequests => Set<AdvanceRequest>();
    public DbSet<AdministrativeDecision> AdministrativeDecisions => Set<AdministrativeDecision>();
    public DbSet<CommencementRecord> CommencementRecords => Set<CommencementRecord>();
    public DbSet<ServiceTransaction> ServiceTransactions => Set<ServiceTransaction>();
    public DbSet<ServiceTransactionStateHistory> ServiceTransactionStateHistories => Set<ServiceTransactionStateHistory>();
    public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
    public DbSet<LeavePolicy> LeavePolicies => Set<LeavePolicy>();
    public DbSet<AdvanceInstallmentPlan> AdvanceInstallmentPlans => Set<AdvanceInstallmentPlan>();
    public DbSet<SeparationChecklist> SeparationChecklists => Set<SeparationChecklist>();
    public DbSet<HireChecklist> HireChecklists => Set<HireChecklist>();
    public DbSet<OrgUnitSnapshot> OrgUnitSnapshots => Set<OrgUnitSnapshot>();
    public DbSet<EmployeeOrgUnitSnapshot> EmployeeOrgUnitSnapshots => Set<EmployeeOrgUnitSnapshot>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<ProcessIntervention> ProcessInterventions => Set<ProcessIntervention>();
    public DbSet<HR.Contracts.Infrastructure.Sagas.SeparationSagaState> SeparationSagaStates =>
        Set<HR.Contracts.Infrastructure.Sagas.SeparationSagaState>();
    public DbSet<HR.Contracts.Infrastructure.Sagas.HireCaseSagaState> HireCaseSagaStates =>
        Set<HR.Contracts.Infrastructure.Sagas.HireCaseSagaState>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ContractType>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.NameAr).HasMaxLength(200);
            entity.Property(e => e.NameEn).HasMaxLength(200);
        });

        modelBuilder.Entity<Contract>(entity =>
        {
            entity.HasOne(e => e.ContractType)
                  .WithMany()
                  .HasForeignKey(e => e.ContractTypeId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Signatures)
                  .WithOne(s => s.Contract)
                  .HasForeignKey(s => s.ContractId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.HasIndex(e => new { e.TenantId, e.EmployeeId })
                .IsUnique()
                .HasDatabaseName("IX_Contracts_OneActivePerEmployee")
                .HasFilter("\"Status\" = 'Active'");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<ContractSignature>(entity =>
        {
            entity.Property(e => e.SignatureType)
                  .HasConversion<string>()
                  .HasMaxLength(50);
        });

        modelBuilder.Entity<ProbationRecord>(entity =>
        {
            entity.HasOne(e => e.Contract)
                  .WithMany()
                  .HasForeignKey(e => e.ContractId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.Result)
                  .HasConversion<string>()
                  .HasMaxLength(50);
        });

        modelBuilder.Entity<SeparationRecord>(entity =>
        {
            entity.HasOne(e => e.Contract)
                  .WithMany()
                  .HasForeignKey(e => e.ContractId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.Property(e => e.SeparationType)
                  .HasConversion<string>()
                  .HasMaxLength(50);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
        });

        modelBuilder.Entity<LeaveRequest>(entity =>
        {
            entity.Property(e => e.LeaveType)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Status })
                .HasDatabaseName("IX_LeaveRequests_Tenant_Employee_Status");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<AdvanceRequest>(entity =>
        {
            entity.Property(e => e.Amount)
                  .HasPrecision(18, 2);

            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<CommencementRecord>(entity =>
        {
            entity.HasOne(e => e.Contract)
                  .WithMany()
                  .HasForeignKey(e => e.ContractId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServiceTransaction>(entity =>
        {
            entity.Property(e => e.TransactionType)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.Property(e => e.State)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.Property(e => e.ReferenceNumber)
                  .HasMaxLength(100);
        });

        modelBuilder.Entity<ServiceTransactionStateHistory>(entity =>
        {
            entity.Property(e => e.FromState)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.Property(e => e.ToState)
                  .HasConversion<string>()
                  .HasMaxLength(50);

            entity.HasOne(e => e.Transaction)
                  .WithMany(t => t.StateHistory)
                  .HasForeignKey(e => e.TransactionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LeaveBalance>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.EmployeeId, e.LeaveType, e.Year }).IsUnique();
            entity.Property(e => e.LeaveType)
                  .HasConversion<string>()
                  .HasMaxLength(50);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<LeavePolicy>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.LeaveType }).IsUnique();
            entity.Property(e => e.LeaveType)
                  .HasConversion<string>()
                  .HasMaxLength(50);
        });

        modelBuilder.Entity<AdvanceInstallmentPlan>(entity =>
        {
            entity.HasOne(e => e.AdvanceRequest)
                  .WithMany()
                  .HasForeignKey(e => e.AdvanceRequestId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.TotalAmount).HasPrecision(18, 2);
            entity.Property(e => e.MonthlyInstallment).HasPrecision(18, 2);
            entity.Property(e => e.RemainingAmount).HasPrecision(18, 2);
        });

        modelBuilder.Entity<SeparationChecklist>(entity =>
        {
            entity.HasOne(e => e.SeparationRecord)
                  .WithMany()
                  .HasForeignKey(e => e.SeparationRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Step)
                  .HasConversion<string>()
                  .HasMaxLength(50);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.Property(e => e.SourceSystem).HasMaxLength(100);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.CompletedByName).HasMaxLength(128);
            entity.HasIndex(e => new { e.SeparationRecordId, e.Step }).IsUnique();
        });

        modelBuilder.Entity<HireChecklist>(entity =>
        {
            entity.HasOne(e => e.ServiceTransaction)
                  .WithMany()
                  .HasForeignKey(e => e.ServiceTransactionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Step)
                  .HasConversion<string>()
                  .HasMaxLength(50);
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.Property(e => e.CompletedByName).HasMaxLength(128);
            entity.HasIndex(e => new { e.ServiceTransactionId, e.Step }).IsUnique();
        });

        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureIdempotencyRecords();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureProcessInterventions();
        modelBuilder.Entity<HR.Contracts.Infrastructure.Sagas.SeparationSagaState>(e =>
        {
            e.ToTable("SeparationSagaStates");
            e.HasKey(x => x.CorrelationId);
            e.Property(x => x.CurrentState).HasMaxLength(64);
            e.Property(x => x.PendingParties).HasMaxLength(500);
            e.Property(x => x.CompletedParties).HasMaxLength(500);
        });
        modelBuilder.Entity<HR.Contracts.Infrastructure.Sagas.HireCaseSagaState>(e =>
        {
            e.ToTable("HireCaseSagaStates");
            e.HasKey(x => x.CorrelationId);
            e.Property(x => x.CurrentState).HasMaxLength(64);
            e.Property(x => x.PendingParties).HasMaxLength(500);
            e.Property(x => x.CompletedParties).HasMaxLength(500);
        });
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
