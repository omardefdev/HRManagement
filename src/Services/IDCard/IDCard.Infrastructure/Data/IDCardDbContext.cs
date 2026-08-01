using IDCard.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace IDCard.Infrastructure.Data;

public class IDCardDbContext : TenantAwareDbContext
{
    public IDCardDbContext(DbContextOptions<IDCardDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<CardDesign> CardDesigns => Set<CardDesign>();
    public DbSet<IssuedCard> IssuedCards => Set<IssuedCard>();
    public DbSet<CardRequest> CardRequests => Set<CardRequest>();
    public DbSet<CardDeliveryRecord> CardDeliveryRecords => Set<CardDeliveryRecord>();
    public DbSet<OfficialSealRecord> OfficialSealRecords => Set<OfficialSealRecord>();
    public DbSet<AccessPermission> AccessPermissions => Set<AccessPermission>();
    public DbSet<CardPermissionSet> CardPermissionSets => Set<CardPermissionSet>();
    public DbSet<CardChecklist> CardChecklists => Set<CardChecklist>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CardDesign>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<IssuedCard>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasOne(e => e.CardDesign)
                  .WithMany()
                  .HasForeignKey(e => e.CardDesignId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DeliveryRecord)
                  .WithOne()
                  .HasForeignKey<IssuedCard>(e => e.DeliveryRecordId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.AccessPermissions)
                  .WithOne(a => a.IssuedCard)
                  .HasForeignKey(a => a.IssuedCardId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CardRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
        });

        modelBuilder.Entity<CardDeliveryRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<CardChecklist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.HasIndex(e => new { e.CorrelationId, e.Step }).IsUnique();
        });

        modelBuilder.Entity<OfficialSealRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<AccessPermission>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<CardPermissionSet>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureIntegrationMessageReceipts();
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
