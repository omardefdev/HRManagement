using Records.Archive.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Records.Archive.Infrastructure.Data;

public class ArchiveDbContext : TenantAwareDbContext
{
    public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<ArchiveItem> ArchiveItems => Set<ArchiveItem>();
    public DbSet<PhysicalFileRecord> PhysicalFileRecords => Set<PhysicalFileRecord>();
    public DbSet<DigitalFileRecord> DigitalFileRecords => Set<DigitalFileRecord>();
    public DbSet<LoanRecord> LoanRecords => Set<LoanRecord>();
    public DbSet<ArchiveIndexEntry> ArchiveIndexEntries => Set<ArchiveIndexEntry>();
    public DbSet<DisposalRecord> DisposalRecords => Set<DisposalRecord>();
    public DbSet<DocumentClassification> DocumentClassifications => Set<DocumentClassification>();
    public DbSet<ArchiveChecklist> ArchiveChecklists => Set<ArchiveChecklist>();
    public DbSet<CustodyEvent> CustodyEvents => Set<CustodyEvent>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ArchiveItem>(entity =>
        {
            entity.HasIndex(e => e.ReferenceNumber).IsUnique();
            entity.Property(e => e.LegalHoldReason).HasMaxLength(500);
            entity.Property(e => e.DisposeCancelReason).HasMaxLength(500);
            entity.Property(e => e.CompletenessAttestation).HasMaxLength(512);

            entity.HasOne(e => e.Classification)
                  .WithMany()
                  .HasForeignKey(e => e.ClassificationId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.PhysicalFiles)
                  .WithOne(e => e.ArchiveItem)
                  .HasForeignKey(e => e.ArchiveItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.DigitalFiles)
                  .WithOne(e => e.ArchiveItem)
                  .HasForeignKey(e => e.ArchiveItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.LoanRecords)
                  .WithOne(e => e.ArchiveItem)
                  .HasForeignKey(e => e.ArchiveItemId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.IndexEntries)
                  .WithOne(e => e.ArchiveItem)
                  .HasForeignKey(e => e.ArchiveItemId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.CustodyEvents)
                  .WithOne(e => e.ArchiveItem)
                  .HasForeignKey(e => e.ArchiveItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DisposalRecord>(entity =>
        {
            entity.HasOne(e => e.ArchiveItem)
                  .WithMany()
                  .HasForeignKey(e => e.ArchiveItemId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentClassification>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
            entity.Property(e => e.RetentionCode).HasMaxLength(64);
        });

        modelBuilder.Entity<ArchiveChecklist>(entity =>
        {
            entity.HasIndex(e => new { e.CorrelationId, e.Step }).IsUnique();
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
        });

        modelBuilder.Entity<CustodyEvent>(entity =>
        {
            entity.Property(e => e.Action).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.HasIndex(e => new { e.ArchiveItemId, e.OccurredAt });
        });

        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
