using IT.Infrastructure.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace IT.Infra.Infrastructure.Data;

public class ITInfraDbContext : TenantAwareDbContext
{
    public ITInfraDbContext(DbContextOptions<ITInfraDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<ServerDevice> ServerDevices => Set<ServerDevice>();
    public DbSet<UserDevice> UserDevices => Set<UserDevice>();
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<SecurityIncident> SecurityIncidents => Set<SecurityIncident>();
    public DbSet<SystemPermissionRecord> SystemPermissionRecords => Set<SystemPermissionRecord>();
    public DbSet<ITSupportTicket> ITSupportTickets => Set<ITSupportTicket>();
    public DbSet<EnvironmentConfiguration> EnvironmentConfigurations => Set<EnvironmentConfiguration>();
    public DbSet<LegacySystemRecord> LegacySystems => Set<LegacySystemRecord>();
    public DbSet<ShadowOperationCheck> ShadowOperationChecks => Set<ShadowOperationCheck>();
    public DbSet<MediaDestructionCertificate> MediaDestructionCertificates => Set<MediaDestructionCertificate>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.Entity<ServerDevice>(e =>
        {
            e.HasIndex(x => x.Hostname).IsUnique();
        });

        // DEC-001
        modelBuilder.Entity<LegacySystemRecord>(e =>
        {
            e.ToTable("LegacySystems");
            e.HasIndex(x => new { x.TenantId, x.SystemName });
            e.HasIndex(x => x.Phase);
            e.Property(x => x.SystemName).HasMaxLength(200);
            e.Property(x => x.Phase).HasMaxLength(32);
            e.Property(x => x.ArchiveManifestHash).HasMaxLength(64);
        });
        modelBuilder.Entity<ShadowOperationCheck>(e =>
        {
            e.ToTable("ShadowOperationChecks");
            e.HasIndex(x => new { x.LegacySystemId, x.AuditDate });
        });
        modelBuilder.Entity<MediaDestructionCertificate>(e =>
        {
            e.ToTable("MediaDestructionCertificates");
            e.HasIndex(x => x.LegacySystemId);
            e.Property(x => x.CertificateHash).HasMaxLength(64);
        });

        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
