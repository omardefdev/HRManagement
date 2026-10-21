using File.Storage.Domain.Entities;
using File.Storage.Domain.Enums;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Files;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace File.Storage.Infrastructure.Data;

public class FileStorageDbContext : TenantAwareDbContext
{
    public FileStorageDbContext(DbContextOptions<FileStorageDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<FileRecord> Files => Set<FileRecord>();
    public DbSet<FileReferenceRecord> FileReferences => Set<FileReferenceRecord>();
    public DbSet<FileAccessGrant> FileAccessGrants => Set<FileAccessGrant>();
    public DbSet<FileAccessAuditLog> FileAccessAuditLogs => Set<FileAccessAuditLog>();
    public DbSet<FilePurgeWorkItem> FilePurgeWorkItems => Set<FilePurgeWorkItem>();
    public DbSet<FileScanWorkItem> FileScanWorkItems => Set<FileScanWorkItem>();
    public DbSet<FileScanResult> FileScanResults => Set<FileScanResult>();
    public DbSet<FileReconcileException> FileReconcileExceptions => Set<FileReconcileException>();
    public DbSet<FileReferenceOutboxMessage> FileReferenceOutbox => Set<FileReferenceOutboxMessage>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<EntityAuditLog> AuditLogs => Set<EntityAuditLog>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.ToTable("Files");
            entity.HasIndex(x => new { x.Bucket, x.ObjectKey }).IsUnique();
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.UploadedAt);
            entity.Property(x => x.IsDeleted).HasDefaultValue(false);
            entity.Property(x => x.ReferenceCount).HasDefaultValue(0);
            entity.Property(x => x.LegalHold).HasDefaultValue(false);
            entity.Property(x => x.ScanStatus).HasDefaultValue(FileScanStatus.PendingScan);
            entity.HasIndex(x => x.ScanStatus);
            entity.ConfigureOptimisticConcurrency(this);
        });

        // SEC-003: object-level grants (not BaseEntity — explicit tenant filter + indexes).
        modelBuilder.Entity<FileAccessGrant>(entity =>
        {
            entity.ToTable("FileAccessGrants");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TenantId, x.FileId });
            entity.HasIndex(x => new { x.TenantId, x.FileReference });
            entity.HasIndex(x => new { x.TenantId, x.HolderType, x.HolderId });
            entity.HasIndex(x => new { x.FileId, x.IsRevoked });
            entity.Property(x => x.Module).HasMaxLength(100).IsRequired();
            entity.Property(x => x.HolderType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.HolderId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.FileReference).HasMaxLength(500).IsRequired();
            entity.Property(x => x.GranteeId).HasMaxLength(200);
            entity.Property(x => x.PresignedUrlMaxLifetimeSeconds).HasDefaultValue(300);
            entity.HasQueryFilter(g =>
                BypassTenantFilter || g.TenantId == EffectiveTenantId);
        });

        modelBuilder.Entity<FileReferenceRecord>(entity =>
        {
            entity.ToTable("FileReferences");
            entity.HasIndex(x => new { x.FileId, x.HolderKey }).IsUnique();
            entity.HasOne(x => x.File)
                .WithMany(x => x.References)
                .HasForeignKey(x => x.FileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FilePurgeWorkItem>(entity =>
        {
            entity.ToTable("FilePurgeWorkItems");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasIndex(x => x.FileId);
            entity.Property(x => x.Status).HasDefaultValue(FilePurgeWorkItemStatus.Pending);
        });

        modelBuilder.Entity<FileScanWorkItem>(entity =>
        {
            entity.ToTable("FileScanWorkItems");
            entity.HasIndex(x => new { x.Status, x.NextAttemptAt });
            entity.HasIndex(x => x.FileId);
            entity.Property(x => x.Status).HasDefaultValue(FileScanWorkItemStatus.Pending);
        });

        modelBuilder.Entity<FileScanResult>(entity =>
        {
            entity.ToTable("FileScanResults");
            entity.HasIndex(x => x.FileId);
            entity.HasIndex(x => x.ScannedAt);
        });

        modelBuilder.Entity<FileReconcileException>(entity =>
        {
            entity.ToTable("FileReconcileExceptions");
            entity.HasIndex(x => new { x.Kind, x.Approved });
            entity.HasIndex(x => x.FileId);
        });

        modelBuilder.Entity<FileAccessAuditLog>(entity =>
        {
            entity.HasIndex(x => x.FileId);
            entity.HasIndex(x => x.OccurredAt);
        });

        modelBuilder.ConfigureFileReferenceOutbox();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureIdempotencyRecords();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
