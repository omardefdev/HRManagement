using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Legal;

public sealed class LegislationSourceConfiguration : IEntityTypeConfiguration<LegislationSource>
{
    public void Configure(EntityTypeBuilder<LegislationSource> builder)
    {
        builder.ToTable("LegislationSources");
        builder.HasKey(e => e.LegalSourceId);

        builder.Property(e => e.LegalSourceId).HasMaxLength(100);
        builder.Property(e => e.InstrumentType).HasMaxLength(32).IsRequired();
        builder.Property(e => e.Number).HasMaxLength(32).IsRequired();
        builder.Property(e => e.TitleAr).HasMaxLength(300).IsRequired();
        builder.Property(e => e.TitleEn).HasMaxLength(300);
        builder.Property(e => e.Status).HasMaxLength(16).IsRequired();
        builder.Property(e => e.Scope).HasMaxLength(500);
        builder.Property(e => e.Owner).HasMaxLength(200).IsRequired();
        builder.Property(e => e.CertifiedTextHash).HasMaxLength(128);
        builder.Property(e => e.RegisterVersion).HasMaxLength(16);

        builder.HasIndex(e => e.Status).HasDatabaseName("IX_LegislationSources_Status");
        builder.HasIndex(e => e.Number).HasDatabaseName("IX_LegislationSources_Number");
    }
}

public sealed class LegislationKeyArticleConfiguration : IEntityTypeConfiguration<LegislationKeyArticle>
{
    public void Configure(EntityTypeBuilder<LegislationKeyArticle> builder)
    {
        builder.ToTable("LegislationKeyArticles");

        builder.HasOne(e => e.LegislationSource)
               .WithMany(s => s.KeyArticles)
               .HasForeignKey(e => e.LegalSourceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.LegalSourceId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Subject).HasMaxLength(300).IsRequired();

        builder.HasIndex(e => new { e.LegalSourceId, e.ArticleNumber })
               .IsUnique()
               .HasDatabaseName("IX_LegislationKeyArticles_Source_Article");
    }
}

public sealed class LegislationInstrumentLinkConfiguration : IEntityTypeConfiguration<LegislationInstrumentLink>
{
    public void Configure(EntityTypeBuilder<LegislationInstrumentLink> builder)
    {
        builder.ToTable("LegislationInstrumentLinks");

        builder.HasOne(e => e.FromLegislationSource)
               .WithMany(s => s.OutgoingLinks)
               .HasForeignKey(e => e.FromLegalSourceId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ToLegislationSource)
               .WithMany(s => s.IncomingLinks)
               .HasForeignKey(e => e.ToLegalSourceId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.Property(e => e.FromLegalSourceId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ToLegalSourceId).HasMaxLength(100).IsRequired();
        builder.Property(e => e.LinkType).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(e => new { e.FromLegalSourceId, e.ToLegalSourceId, e.LinkType })
               .IsUnique()
               .HasDatabaseName("IX_LegislationInstrumentLinks_From_To_Type");
    }
}

public sealed class LegislationSourceAliasConfiguration : IEntityTypeConfiguration<LegislationSourceAlias>
{
    public void Configure(EntityTypeBuilder<LegislationSourceAlias> builder)
    {
        builder.ToTable("LegislationSourceAliases");

        builder.HasOne(e => e.LegislationSource)
               .WithMany(s => s.Aliases)
               .HasForeignKey(e => e.LegalSourceId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Alias).HasMaxLength(100);
        builder.Property(e => e.LegalSourceId).HasMaxLength(100).IsRequired();

        builder.HasIndex(e => e.LegalSourceId).HasDatabaseName("IX_LegislationSourceAliases_LegalSourceId");
    }
}
