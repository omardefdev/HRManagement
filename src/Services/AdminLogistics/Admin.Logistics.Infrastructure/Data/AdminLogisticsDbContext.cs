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
    public DbSet<AssetLossReport> AssetLossReports => Set<AssetLossReport>();
    public DbSet<SupplyOrder> SupplyOrders => Set<SupplyOrder>();
    public DbSet<SupplyOrderLine> SupplyOrderLines => Set<SupplyOrderLine>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<MaintenanceRecord> MaintenanceRecords => Set<MaintenanceRecord>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<ReceiptRecord> ReceiptRecords => Set<ReceiptRecord>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<EmployeeOrgUnitSnapshot> EmployeeOrgUnitSnapshots => Set<EmployeeOrgUnitSnapshot>();
    public DbSet<OrgUnitSnapshot> OrgUnitSnapshots => Set<OrgUnitSnapshot>();
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

            entity.HasOne(a => a.OriginReceiptRecord)
                  .WithMany()
                  .HasForeignKey(a => a.OriginReceiptRecordId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(a => a.OriginSupplyOrderLine)
                  .WithMany()
                  .HasForeignKey(a => a.OriginSupplyOrderLineId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.Property(a => a.LegacyEntryReason).HasMaxLength(256);
        });

        modelBuilder.Entity<AssetAssignment>(entity =>
        {
            entity.Property(a => a.EvidenceRef).HasMaxLength(256);
            entity.Property(a => a.HandoverConditionNotes).HasMaxLength(256);
            entity.Property(a => a.ReturnNotes).HasMaxLength(256);
            entity.HasIndex(a => a.AssetId)
                .IsUnique()
                .HasFilter("\"ReturnedAt\" IS NULL");

            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<EmployeeOrgUnitSnapshot>(entity =>
        {
            entity.HasKey(e => e.EmployeeId);
            entity.HasIndex(e => e.PrimaryOrgUnitId);
        });

        modelBuilder.Entity<OrgUnitSnapshot>(entity =>
        {
            entity.HasIndex(o => o.ParentId);
            entity.HasIndex(o => new { o.TenantId, o.MaterializedPath });
        });

        modelBuilder.Entity<AssetLossReport>(entity =>
        {
            entity.Property(r => r.Notes).HasMaxLength(256);
            entity.Property(r => r.EvidenceRef).HasMaxLength(256);
            entity.Property(r => r.RecoveryNotes).HasMaxLength(256);
            entity.Property(r => r.BeforeStateJson);
            entity.Property(r => r.AfterStateJson);
            entity.Property(r => r.SettlementApprovedBy).HasMaxLength(256);

            entity.HasOne(r => r.Asset)
                .WithMany(a => a.LossReports)
                .HasForeignKey(r => r.AssetId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(r => r.AssetId)
                .IsUnique()
                .HasFilter("\"Status\" = 0");
        });

        modelBuilder.Entity<SupplyOrder>(entity =>
        {
            entity.HasMany(o => o.Lines)
                  .WithOne(l => l.SupplyOrder)
                  .HasForeignKey(l => l.SupplyOrderId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplyOrderLine>(entity =>
        {
            entity.HasOne(l => l.InventoryItem)
                  .WithMany()
                  .HasForeignKey(l => l.InventoryItemId)
                  .OnDelete(DeleteBehavior.SetNull);
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

            entity.HasOne(r => r.InventoryItem)
                  .WithMany()
                  .HasForeignKey(r => r.InventoryItemId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
