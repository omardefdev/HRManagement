using HRManagement.SharedKernel.Evidence;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Evidence;

public static class EvidenceEntityConfiguration
{
    public static void ConfigureEvidenceRecords(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidenceRecord>(entity =>
        {
            entity.ToTable("EvidenceRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CausationId).HasMaxLength(128);
            entity.Property(e => e.Actor).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(256);
            entity.Property(e => e.Resource).HasMaxLength(512).IsRequired();
            entity.Property(e => e.Purpose).HasMaxLength(256).IsRequired();
            entity.Property(e => e.AssuranceLevel).HasMaxLength(64);
            entity.Property(e => e.Policy).HasMaxLength(128);
            entity.Property(e => e.Rule).HasMaxLength(128);
            entity.Property(e => e.Reason).HasMaxLength(2000);
            entity.Property(e => e.Result).HasMaxLength(64).IsRequired();
            entity.Property(e => e.BeforeHash).HasMaxLength(128);
            entity.Property(e => e.AfterHash).HasMaxLength(128);
            entity.Property(e => e.ReleaseVersion).HasMaxLength(64);
            entity.Property(e => e.ConfigVersion).HasMaxLength(64);
            entity.Property(e => e.TimeSource).HasMaxLength(64).IsRequired();
            entity.Property(e => e.TimeZoneId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ChainKey).HasMaxLength(64).IsRequired();
            entity.Property(e => e.PrevHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.EventHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(e => new { e.TenantId, e.CorrelationId });
            entity.HasIndex(e => new { e.ChainKey, e.Sequence }).IsUnique();
        });
    }

    /// <summary>EVD-003: configure immutable evidence artifacts (call from any hosting DbContext).</summary>
    public static void ConfigureEvidenceArtifacts(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidenceArtifact>(entity =>
        {
            entity.ToTable("EvidenceArtifacts");
            entity.HasKey(e => e.Id);
            entity.Ignore(e => e.IsExpired);
            entity.Property(e => e.ArtifactId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.CardId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ControlId).HasMaxLength(128);
            entity.Property(e => e.ReleaseDigest).HasMaxLength(128).IsRequired();
            entity.Property(e => e.SourceType).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ContentHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Signature).HasMaxLength(2048);
            entity.Property(e => e.Classification).HasMaxLength(64).IsRequired();
            entity.Property(e => e.StorageUri).HasMaxLength(1024).IsRequired();
            entity.HasIndex(e => e.ArtifactId).IsUnique();
            entity.HasIndex(e => e.CardId);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasIndex(e => e.ContentHash);
        });

        modelBuilder.Entity<EvidencePackExport>(entity =>
        {
            entity.ToTable("EvidencePackExports");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PackId).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExportedBy).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ArtifactIdsJson).IsRequired();
            entity.Property(e => e.PackHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Destination).HasMaxLength(512).IsRequired();
            entity.HasIndex(e => e.PackId).IsUnique();
            entity.HasIndex(e => e.ExportedAtUtc);
        });
    }
}
