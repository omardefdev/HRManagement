using HRManagement.SharedKernel.Signatures;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.ServiceDefaults.Signatures;

public static class SignatureEntityConfiguration
{
    public static void ConfigureSignatureRecords(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SignatureRecord>(entity =>
        {
            entity.ToTable("SignatureRecords");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SubjectType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.SubjectId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SignerSubject).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SignerRole).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Intent).HasMaxLength(64).IsRequired();
            entity.Property(e => e.CertificateThumbprint).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CanonicalPayloadHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.SignatureBase64).IsRequired();
            entity.Property(e => e.Algorithm).HasMaxLength(64).IsRequired();
            entity.Property(e => e.LtvStatus).HasMaxLength(32).IsRequired();

            entity.HasIndex(e => new { e.TenantId, e.SubjectType, e.SubjectId });
            entity.HasIndex(e => new { e.TenantId, e.SignerSubject });
            entity.HasIndex(e => e.CertificateThumbprint);
        });
    }
}
