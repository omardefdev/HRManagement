using Admin.Logistics.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Admin.Logistics.Infrastructure.Data;

public class AdminLogisticsDbContext : TenantAwareDbContext
{
    public AdminLogisticsDbContext(DbContextOptions<AdminLogisticsDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();
    public DbSet<SupplyOrder> SupplyOrders => Set<SupplyOrder>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<ReceiptRecord> ReceiptRecords => Set<ReceiptRecord>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureIntegrationMessageReceipts();

        modelBuilder.Entity<Asset>(entity =>
        {
            entity.HasIndex(a => new { a.TenantId, a.TagNumber }).IsUnique();
            entity.Property(a => a.CancellationReason).HasMaxLength(500);

            entity.HasMany(a => a.Assignments)
                  .WithOne(aa => aa.Asset)
                  .HasForeignKey(aa => aa.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(a => a.MaintenanceRecords)
                  .WithOne(m => m.Asset)
                  .HasForeignKey(m => m.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // FUN-005: at most one open assignment per asset (ReturnedAt IS NULL).
        modelBuilder.Entity<AssetAssignment>(entity =>
        {
            entity.Property(a => a.EvidenceRef).HasMaxLength(256);
            entity.HasIndex(a => a.AssetId)
                .IsUnique()
                .HasFilter("\"ReturnedAt\" IS NULL");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.HasIndex(i => new { i.TenantId, i.Sku }).IsUnique();
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<ReceiptRecord>(entity =>
        {
            entity.HasOne(r => r.SupplyOrder)
                  .WithMany()
                  .HasForeignKey(r => r.SupplyOrderId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
