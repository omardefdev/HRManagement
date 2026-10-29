using Dev.Analytics.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Dev.Analytics.Infrastructure.Data;

public class AnalyticsDbContext : TenantAwareDbContext
{
    public AnalyticsDbContext(DbContextOptions<AnalyticsDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<ETLJob> ETLJobs => Set<ETLJob>();
    public DbSet<ReadModel> ReadModels => Set<ReadModel>();
    public DbSet<KPI> KPIs => Set<KPI>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    /// <summary>HCM-032 (PLAN-32): closed recruitment requisitions (aggregates from HR.Core).</summary>
    public DbSet<RecruitmentRequisitionFact> RecruitmentRequisitionFacts => Set<RecruitmentRequisitionFact>();
    public DbSet<DataRefreshSchedule> DataRefreshSchedules => Set<DataRefreshSchedule>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<KPI>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<DataRefreshSchedule>(entity =>
        {
            entity.HasOne(e => e.TargetReadModel)
                  .WithMany()
                  .HasForeignKey(e => e.TargetReadModelId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AnalyticsEvent>(entity =>
        {
            entity.Property(e => e.IdempotencyKey).HasMaxLength(128);
            entity.HasIndex(e => new { e.TenantId, e.EventName, e.OrgUnitId });
            entity.HasIndex(e => e.IdempotencyKey);
        });

        modelBuilder.Entity<RecruitmentRequisitionFact>(entity =>
        {
            entity.Property(e => e.Outcome).HasMaxLength(16);
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.ClosedAt });
        });

        modelBuilder.Entity<Report>(entity =>
        {
            entity.Property(e => e.ScopeGrain).HasMaxLength(32);
            entity.Property(e => e.SourceEvent).HasMaxLength(64);
            entity.Property(e => e.LastFingerprint).HasMaxLength(128);
        });

        modelBuilder.Entity<ReadModel>(entity =>
        {
            entity.Property(e => e.LastFingerprint).HasMaxLength(128);
            entity.Property(e => e.SourceEventName).HasMaxLength(64);
        });

        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<HRManagement.SharedKernel.Entities.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
