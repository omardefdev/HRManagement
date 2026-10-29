using HR.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Recruitment;

/// <summary>HCM-031 (PLAN-31): results versions, results, allocation and line reserve (migration HCM031_Results).</summary>
public sealed class CompetitionResultVersionConfiguration : IEntityTypeConfiguration<CompetitionResultVersion>
{
    public void Configure(EntityTypeBuilder<CompetitionResultVersion> builder)
    {
        builder.HasIndex(e => new { e.CompetitionId, e.VersionNo }).IsUnique();
        // At most one published version per competition (the previous one is superseded in the same save).
        builder.HasIndex(e => e.CompetitionId)
               .IsUnique()
               .HasFilter("\"Status\" = 'Published'")
               .HasDatabaseName("IX_CompetitionResultVersions_OnePublished");
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Reason).HasMaxLength(1000);
        builder.Property(e => e.ReturnReason).HasMaxLength(500);
        builder.Property(e => e.Sha256).HasMaxLength(64).IsFixedLength();
        builder.HasOne(e => e.Competition)
               .WithMany()
               .HasForeignKey(e => e.CompetitionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Results)
               .WithOne(r => r.ResultVersion)
               .HasForeignKey(r => r.ResultVersionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompetitionResultConfiguration : IEntityTypeConfiguration<CompetitionResult>
{
    public void Configure(EntityTypeBuilder<CompetitionResult> builder)
    {
        builder.HasIndex(e => new { e.ResultVersionId, e.RecruitmentApplicationId }).IsUnique();
        builder.HasIndex(e => new { e.ResultVersionId, e.ExamNumber }).IsUnique();
        builder.HasIndex(e => e.RecruitmentApplicationId);
        builder.Property(e => e.ExamNumber).HasMaxLength(16);
        builder.Property(e => e.ExamScore).HasPrecision(7, 2);
        builder.Property(e => e.InterviewScore).HasPrecision(7, 2);
        builder.Property(e => e.FinalScore).HasPrecision(7, 2);
        builder.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.TieDecisionReason).HasMaxLength(500);
        builder.HasOne(e => e.RecruitmentApplication)
               .WithMany()
               .HasForeignKey(e => e.RecruitmentApplicationId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompetitionAllocationConfiguration : IEntityTypeConfiguration<CompetitionAllocation>
{
    public void Configure(EntityTypeBuilder<CompetitionAllocation> builder)
    {
        builder.HasIndex(e => new { e.CompetitionId, e.RecruitmentApplicationId });
        builder.HasIndex(e => e.AllocatedRequisitionId);
        builder.HasOne(e => e.RecruitmentApplication)
               .WithMany()
               .HasForeignKey(e => e.RecruitmentApplicationId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AllocatedLine)
               .WithMany()
               .HasForeignKey(e => e.AllocatedLineId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.AllocatedRequisition)
               .WithMany()
               .HasForeignKey(e => e.AllocatedRequisitionId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RecruitmentCompetition>()
               .WithMany()
               .HasForeignKey(e => e.CompetitionId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class CompetitionReserveEntryConfiguration : IEntityTypeConfiguration<CompetitionReserveEntry>
{
    public void Configure(EntityTypeBuilder<CompetitionReserveEntry> builder)
    {
        builder.HasIndex(e => new { e.CompetitionLineId, e.RecruitmentApplicationId }).IsUnique();
        builder.HasIndex(e => new { e.CompetitionLineId, e.Status, e.LineRank });
        builder.HasIndex(e => e.RecruitmentApplicationId);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(e => e.CompetitionLine)
               .WithMany()
               .HasForeignKey(e => e.CompetitionLineId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.RecruitmentApplication)
               .WithMany()
               .HasForeignKey(e => e.RecruitmentApplicationId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
