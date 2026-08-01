using HRManagement.Gateway.Models;
using HRManagement.ServiceDefaults.Evidence;
using HRManagement.SharedKernel.Evidence;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.Gateway.Data;

public sealed class GatewayAuditDbContext(DbContextOptions<GatewayAuditDbContext> options) : DbContext(options)
{
    public DbSet<GatewayAuditEntry> AuditEntries => Set<GatewayAuditEntry>();
    public DbSet<SecurityAuditEvent> SecurityAuditEvents => Set<SecurityAuditEvent>();
    public DbSet<AuditLegalHold> AuditLegalHolds => Set<AuditLegalHold>();

    /// <summary>EVD-001 unified decision evidence (hosted on gateway audit store).</summary>
    public DbSet<EvidenceRecord> EvidenceRecords => Set<EvidenceRecord>();

    /// <summary>EVD-003 immutable evidence artifacts.</summary>
    public DbSet<EvidenceArtifact> EvidenceArtifacts => Set<EvidenceArtifact>();

    public DbSet<EvidencePackExport> EvidencePackExports => Set<EvidencePackExport>();
    public DbSet<GatewayAuditChainState> AuditChainStates => Set<GatewayAuditChainState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var e = modelBuilder.Entity<GatewayAuditEntry>();
        e.ToTable("gateway_audit_entries");
        e.HasKey(x => x.Id);
        e.Property(x => x.CorrelationId).HasMaxLength(128);
        e.Property(x => x.UserSub).HasMaxLength(128);
        e.Property(x => x.UserName).HasMaxLength(256);
        e.Property(x => x.Method).HasMaxLength(16);
        e.Property(x => x.Path).HasMaxLength(512);
        e.Property(x => x.QueryString).HasMaxLength(512);
        e.Property(x => x.Decision).HasMaxLength(32);
        e.Property(x => x.ClientIp).HasMaxLength(128);
        e.Property(x => x.UserAgent).HasMaxLength(512);
        e.Property(x => x.ChainKey).HasMaxLength(64).HasDefaultValue("default");
        e.Property(x => x.PrevHash).HasMaxLength(128);
        e.Property(x => x.EventHash).HasMaxLength(128);
        e.HasIndex(x => x.OccurredAtUtc);
        e.HasIndex(x => new { x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_gateway_audit_entries_OccurredAt_Id");
        e.HasIndex(x => x.CorrelationId);
        e.HasIndex(x => x.StatusCode);
        e.HasIndex(x => x.SequenceNumber);
        e.HasIndex(x => new { x.ChainKey, x.SequenceNumber });
        e.HasIndex(x => x.EventHash);

        var s = modelBuilder.Entity<SecurityAuditEvent>();
        s.ToTable("security_audit_events");
        s.HasKey(x => x.Id);
        s.Property(x => x.EventType).HasMaxLength(64);
        s.Property(x => x.ActorSub).HasMaxLength(128);
        s.Property(x => x.CorrelationId).HasMaxLength(128);
        s.Property(x => x.Resource).HasMaxLength(512);
        s.Property(x => x.DetailJson).HasMaxLength(4000);
        s.HasIndex(x => x.OccurredAtUtc);
        s.HasIndex(x => x.EventType);

        var h = modelBuilder.Entity<AuditLegalHold>();
        h.ToTable("audit_legal_holds");
        h.HasKey(x => x.Id);
        h.Property(x => x.Reason).HasMaxLength(512);
        h.Property(x => x.CorrelationId).HasMaxLength(128);
        h.Property(x => x.UserSub).HasMaxLength(128);
        h.Property(x => x.CreatedBy).HasMaxLength(128);

        modelBuilder.ConfigureEvidenceRecords();
        modelBuilder.ConfigureEvidenceArtifacts();

        modelBuilder.Entity<GatewayAuditChainState>(entity =>
        {
            entity.ToTable("gateway_audit_chain_state");
            entity.HasKey(x => x.ChainKey);
            entity.Property(x => x.ChainKey).HasMaxLength(64);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectAuditMutations();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        RejectAuditMutations();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void RejectAuditMutations()
    {
        foreach (var entry in ChangeTracker.Entries<GatewayAuditEntry>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "gateway_audit_entries are append-only; updates and deletes are not allowed.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<SecurityAuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "security_audit_events are append-only; updates and deletes are not allowed.");
            }
        }
    }
}
