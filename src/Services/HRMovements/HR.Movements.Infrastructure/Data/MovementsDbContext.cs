using HR.Movements.Domain.Entities;
using HR.Movements.Infrastructure.Sagas;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Movements.Infrastructure.Data;

public class MovementsDbContext : TenantAwareDbContext
{
    public MovementsDbContext(DbContextOptions<MovementsDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<TransferRequest> TransferRequests => Set<TransferRequest>();
    public DbSet<MovementNotice> MovementNotices => Set<MovementNotice>();
    public DbSet<DistributionPlan> DistributionPlans => Set<DistributionPlan>();
    public DbSet<MovementExecutionRecord> MovementExecutionRecords => Set<MovementExecutionRecord>();
    public DbSet<SecondmentRequest> SecondmentRequests => Set<SecondmentRequest>();
    public DbSet<LoanAssignmentRequest> LoanAssignmentRequests => Set<LoanAssignmentRequest>();
    public DbSet<PromotionRequest> PromotionRequests => Set<PromotionRequest>();
    public DbSet<DelegationRequest> DelegationRequests => Set<DelegationRequest>();
    public DbSet<TransferApproval> TransferApprovals => Set<TransferApproval>();
    public DbSet<TransferFinancialImpact> TransferFinancialImpacts => Set<TransferFinancialImpact>();
    public DbSet<MovementChecklist> MovementChecklists => Set<MovementChecklist>();
    public DbSet<OrgUnitSnapshot> OrgUnitSnapshots => Set<OrgUnitSnapshot>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<ProcessIntervention> ProcessInterventions => Set<ProcessIntervention>();
    public DbSet<HR.Movements.Infrastructure.Sagas.TransferSagaState> TransferSagaStates =>
        Set<HR.Movements.Infrastructure.Sagas.TransferSagaState>();
    public DbSet<HR.Movements.Infrastructure.Sagas.SecondmentSagaState> SecondmentSagaStates =>
        Set<HR.Movements.Infrastructure.Sagas.SecondmentSagaState>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TransferRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.SourceLegalRegimeId).HasMaxLength(50);
            entity.Property(e => e.TargetLegalRegimeId).HasMaxLength(50);
            entity.Property(e => e.TransitionDecisionRef).HasMaxLength(200);
            entity.Property(e => e.ConditionCode).HasMaxLength(100);
            entity.Property(e => e.ConflictNotes).HasMaxLength(1000);

            entity.HasMany(e => e.ExecutionRecords)
                  .WithOne(r => r.TransferRequest)
                  .HasForeignKey(r => r.TransferRequestId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Approvals)
                  .WithOne(a => a.TransferRequest)
                  .HasForeignKey(a => a.TransferRequestId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.FinancialImpacts)
                  .WithOne(f => f.TransferRequest)
                  .HasForeignKey(f => f.TransferRequestId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.TenantId, e.Status })
                .HasDatabaseName("IX_TransferRequests_Tenant_Status");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<MovementNotice>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<DistributionPlan>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.TransferRequest)
                  .WithMany()
                  .HasForeignKey(e => e.TransferRequestId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MovementExecutionRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<SecondmentRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.HostOrg).HasMaxLength(256);

            entity.HasMany(e => e.ExecutionRecords)
                  .WithOne(r => r.SecondmentRequest)
                  .HasForeignKey(r => r.SecondmentRequestId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LoanAssignmentRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);

            entity.HasMany(e => e.ExecutionRecords)
                  .WithOne(r => r.LoanAssignmentRequest)
                  .HasForeignKey(r => r.LoanAssignmentRequestId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PromotionRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.SourceLegalRegimeId).HasMaxLength(50);
            entity.Property(e => e.TransitionDecisionRef).HasMaxLength(200);

            entity.HasMany(e => e.ExecutionRecords)
                  .WithOne(r => r.PromotionRequest)
                  .HasForeignKey(r => r.PromotionRequestId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DelegationRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.HostOrg).HasMaxLength(256);

            entity.HasMany(e => e.ExecutionRecords)
                  .WithOne(r => r.DelegationRequest)
                  .HasForeignKey(r => r.DelegationRequestId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TransferApproval>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<TransferFinancialImpact>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<MovementChecklist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.Property(e => e.CompletedByName).HasMaxLength(128);
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.HasIndex(e => new { e.MovementKind, e.CorrelationId, e.Step }).IsUnique();
            entity.HasIndex(e => e.CorrelationId);
        });

        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureIdempotencyRecords();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureProcessInterventions();
        modelBuilder.Entity<TransferSagaState>(e =>
        {
            e.ToTable("TransferSagaStates");
            e.HasKey(x => x.CorrelationId);
            e.Property(x => x.CurrentState).HasMaxLength(64);
            e.Property(x => x.PendingParties).HasMaxLength(500);
            e.Property(x => x.CompletedParties).HasMaxLength(500);
            e.Property(x => x.SourceLegalRegimeId).HasMaxLength(50);
            e.Property(x => x.TargetLegalRegimeId).HasMaxLength(50);
            e.Property(x => x.TransitionDecisionRef).HasMaxLength(200);
            e.Property(x => x.ConditionCode).HasMaxLength(100);
            e.Property(x => x.ConflictNotes).HasMaxLength(1000);
        });
        modelBuilder.Entity<SecondmentSagaState>(e =>
        {
            e.ToTable("SecondmentSagaStates");
            e.HasKey(x => x.CorrelationId);
            e.Property(x => x.CurrentState).HasMaxLength(64);
            e.Property(x => x.PendingParties).HasMaxLength(500);
            e.Property(x => x.CompletedParties).HasMaxLength(500);
            e.Property(x => x.HostOrg).HasMaxLength(256);
        });
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
