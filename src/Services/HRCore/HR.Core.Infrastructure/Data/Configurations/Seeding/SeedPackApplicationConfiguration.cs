using HR.Core.Domain.Entities.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Seeding;

public class SeedPackApplicationConfiguration : IEntityTypeConfiguration<SeedPackApplication>
{
    public void Configure(EntityTypeBuilder<SeedPackApplication> builder)
    {
        builder.HasIndex(e => new { e.TenantId, e.PackId, e.ReleaseDigest }).IsUnique();
        builder.Property(e => e.PackId).HasMaxLength(100);
        builder.Property(e => e.ReleaseDigest).HasMaxLength(128);
        builder.Property(e => e.RollbackRef).HasMaxLength(200);
    }
}
