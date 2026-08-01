using HRManagement.SharedKernel.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.MasterData;

/// <summary>MDM-001: EF Core configuration for <see cref="ReferenceCatalogEntry"/>.</summary>
public class ReferenceCatalogEntryConfiguration : IEntityTypeConfiguration<ReferenceCatalogEntry>
{
    public void Configure(EntityTypeBuilder<ReferenceCatalogEntry> builder)
    {
        builder.ToTable("ReferenceCatalogEntries");

        builder.HasIndex(e => new { e.TenantId, e.CatalogKind, e.Code })
               .IsUnique()
               .HasDatabaseName("IX_RefCatalog_Tenant_Kind_Code");

        builder.HasIndex(e => new { e.CatalogKind, e.IsActive })
               .HasDatabaseName("IX_RefCatalog_Kind_Active");

        builder.Property(e => e.CatalogKind)
               .HasConversion<string>()
               .HasMaxLength(50);

        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.NameAr).HasMaxLength(300).IsRequired();
        builder.Property(e => e.NameEn).HasMaxLength(300);
        builder.Property(e => e.NameFr).HasMaxLength(300);
        builder.Property(e => e.ReplacedByCode).HasMaxLength(50);
        builder.Property(e => e.SourceAuthority).HasMaxLength(200);
        builder.Property(e => e.StewardRole).HasMaxLength(100);
    }
}
