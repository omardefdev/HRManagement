using HR.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Recruitment;

/// <summary>HCM-031 (PLAN-31): recruitment competitions (migration HCM031_Competitions).</summary>
public sealed class RecruitmentCompetitionConfiguration : IEntityTypeConfiguration<RecruitmentCompetition>
{
    public void Configure(EntityTypeBuilder<RecruitmentCompetition> builder)
    {
        builder.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Status });
        builder.Property(e => e.Number).HasMaxLength(50);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.LegalSourceId).HasMaxLength(100);
        builder.Property(e => e.ApplicationConditions).HasMaxLength(2000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.PublishingChannel).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ReconcileRule).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.TieBreakOrder).HasMaxLength(100);
        builder.Property(e => e.PassMark).HasPrecision(7, 2);
        builder.Property(e => e.ReconcileThreshold).HasPrecision(7, 2);
        builder.Property(e => e.ReturnReason).HasMaxLength(500);
        builder.Property(e => e.CancellationReason).HasMaxLength(500);
        builder.HasOne(e => e.LegalSource)
               .WithMany()
               .HasForeignKey(e => e.LegalSourceId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.PublicJobPosting)
               .WithMany()
               .HasForeignKey(e => e.PublicJobPostingId)
               .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.InternalVacancy)
               .WithMany()
               .HasForeignKey(e => e.InternalVacancyId)
               .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(e => e.Lines)
               .WithOne(l => l.Competition)
               .HasForeignKey(l => l.CompetitionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_RecruitmentCompetitions_ObjectionWindowDays", "\"ObjectionWindowDays\" >= 1 AND \"ObjectionWindowDays\" <= 60");
            t.HasCheckConstraint("CK_RecruitmentCompetitions_ReserveValidityMonths", "\"ReserveValidityMonths\" >= 0 AND \"ReserveValidityMonths\" <= 36");
            t.HasCheckConstraint("CK_RecruitmentCompetitions_MaxLinesPerCandidate", "\"MaxLinesPerCandidate\" >= 1");
        });
    }
}

public sealed class CompetitionLineConfiguration : IEntityTypeConfiguration<CompetitionLine>
{
    public void Configure(EntityTypeBuilder<CompetitionLine> builder)
    {
        builder.HasIndex(e => new { e.CompetitionId, e.LineNo }).IsUnique();
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.GradeCode).HasMaxLength(20);
        builder.Property(e => e.LinePassMark).HasPrecision(7, 2);
        builder.HasOne(e => e.JobCatalogEntry)
               .WithMany()
               .HasForeignKey(e => e.JobCatalogEntryId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.OrgUnit)
               .WithMany()
               .HasForeignKey(e => e.OrgUnitId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.CriteriaVersion)
               .WithMany()
               .HasForeignKey(e => e.CriteriaVersionId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Requisitions)
               .WithOne(r => r.CompetitionLine)
               .HasForeignKey(r => r.CompetitionLineId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompetitionPreferenceConfiguration : IEntityTypeConfiguration<CompetitionPreference>
{
    public void Configure(EntityTypeBuilder<CompetitionPreference> builder)
    {
        builder.HasIndex(e => new { e.RecruitmentApplicationId, e.CompetitionLineId }).IsUnique();
        builder.HasIndex(e => new { e.RecruitmentApplicationId, e.Order }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.CompetitionLineId });
        builder.Property(e => e.EligibilityFlags).HasMaxLength(200);
        builder.HasOne(e => e.RecruitmentApplication)
               .WithMany()
               .HasForeignKey(e => e.RecruitmentApplicationId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.CompetitionLine)
               .WithMany()
               .HasForeignKey(e => e.CompetitionLineId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(t => t.HasCheckConstraint("CK_CompetitionPreferences_Order", "\"Order\" >= 1"));
    }
}
