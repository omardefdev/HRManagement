using HR.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Recruitment;

/// <summary>HCM-031 (PLAN-31): exam sessions, seated candidates, markers and marks (migration HCM031_Exams).</summary>
public sealed class ExamSessionConfiguration : IEntityTypeConfiguration<ExamSession>
{
    public void Configure(EntityTypeBuilder<ExamSession> builder)
    {
        builder.HasIndex(e => new { e.CompetitionId, e.Type, e.ScheduledAt });
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Venue).HasMaxLength(300);
        builder.Property(e => e.MaxScore).HasPrecision(7, 2);
        builder.HasOne(e => e.Competition)
               .WithMany()
               .HasForeignKey(e => e.CompetitionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Candidates)
               .WithOne(c => c.ExamSession)
               .HasForeignKey(c => c.ExamSessionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(e => e.Markers)
               .WithOne(m => m.ExamSession)
               .HasForeignKey(m => m.ExamSessionId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_ExamSessions_Capacity", "\"Capacity\" >= 1");
            t.HasCheckConstraint("CK_ExamSessions_MaxScore", "\"MaxScore\" > 0");
        });
    }
}

public sealed class ExamCandidateConfiguration : IEntityTypeConfiguration<ExamCandidate>
{
    public void Configure(EntityTypeBuilder<ExamCandidate> builder)
    {
        builder.HasIndex(e => new { e.CompetitionId, e.ExamType, e.ExamNumber }).IsUnique();
        builder.HasIndex(e => new { e.CompetitionId, e.ExamType, e.RecruitmentApplicationId }).IsUnique();
        builder.HasIndex(e => new { e.ExamSessionId, e.SeatNumber }).IsUnique();
        builder.HasIndex(e => e.RecruitmentApplicationId);
        builder.Property(e => e.ExamType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Attendance).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.ExamNumber).HasMaxLength(16);
        builder.Property(e => e.FinalMark).HasPrecision(7, 2);
        builder.Property(e => e.AdmissionCardSha256).HasMaxLength(64).IsFixedLength();
        builder.HasOne(e => e.RecruitmentApplication)
               .WithMany()
               .HasForeignKey(e => e.RecruitmentApplicationId)
               .OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(e => e.Scores)
               .WithOne(s => s.ExamCandidate)
               .HasForeignKey(s => s.ExamCandidateId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ExamMarkerAssignmentConfiguration : IEntityTypeConfiguration<ExamMarkerAssignment>
{
    public void Configure(EntityTypeBuilder<ExamMarkerAssignment> builder)
    {
        builder.HasIndex(e => new { e.ExamSessionId, e.Role }).IsUnique();
        builder.HasIndex(e => new { e.ExamSessionId, e.MarkerEmployeeId })
               .IsUnique()
               .HasFilter("\"MarkerEmployeeId\" IS NOT NULL");
        builder.HasIndex(e => new { e.TenantId, e.MarkerEmployeeId });
        builder.HasIndex(e => new { e.TenantId, e.MarkerUserId });
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(e => e.MarkerEmployee)
               .WithMany()
               .HasForeignKey(e => e.MarkerEmployeeId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExamScoreConfiguration : IEntityTypeConfiguration<ExamScore>
{
    public void Configure(EntityTypeBuilder<ExamScore> builder)
    {
        // One mark per paper and round; objection corrections may be repeated (one per upheld objection).
        builder.HasIndex(e => new { e.ExamCandidateId, e.Round })
               .IsUnique()
               .HasFilter("\"Round\" <> 'ObjectionCorrection'");
        builder.Property(e => e.Round).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Score).HasPrecision(7, 2);
        builder.Property(e => e.Note).HasMaxLength(500);
    }
}
