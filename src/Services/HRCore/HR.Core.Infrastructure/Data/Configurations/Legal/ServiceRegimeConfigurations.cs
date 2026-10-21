using HRManagement.SharedKernel.Benefits;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Legal;

public sealed class LegalRegimeConfiguration : IEntityTypeConfiguration<LegalRegime>
{
    public void Configure(EntityTypeBuilder<LegalRegime> builder)
    {
        builder.ToTable("LegalRegimes");
        builder.HasKey(e => e.LegalRegimeId);

        builder.Property(e => e.LegalRegimeId).HasMaxLength(50);
        builder.Property(e => e.RegimeName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Type).HasConversion<int>();
        builder.Property(e => e.LegislationRef).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ResolvedLegalSourceId).HasMaxLength(100);
        builder.Property(e => e.AllowedEmploymentTypes).HasMaxLength(500);
        builder.Property(e => e.GradeStructureId).HasMaxLength(100);
        builder.Property(e => e.TransitionDecisionRef).HasMaxLength(200);
        builder.Property(e => e.ConflictNotes).HasMaxLength(1000);

        builder.HasOne(e => e.ResolvedLegalSource)
               .WithMany()
               .HasForeignKey(e => e.ResolvedLegalSourceId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => e.ResolvedLegalSourceId).HasDatabaseName("IX_LegalRegimes_ResolvedLegalSourceId");
        builder.HasIndex(e => e.IsActive).HasDatabaseName("IX_LegalRegimes_IsActive");
    }
}

public sealed class BenefitPlanConfiguration : IEntityTypeConfiguration<BenefitPlan>
{
    public void Configure(EntityTypeBuilder<BenefitPlan> builder)
    {
        builder.ToTable("BenefitPlans");

        builder.Property(e => e.PlanCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.PlanName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.PlanType).HasConversion<int>();
        builder.Property(e => e.ProviderName).HasMaxLength(200);
        builder.Property(e => e.LegalSourceId).HasMaxLength(100);
        builder.Property(e => e.LegalRegimeId).HasMaxLength(50);

        builder.HasOne(e => e.LegalSource)
               .WithMany()
               .HasForeignKey(e => e.LegalSourceId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(e => e.LegalRegime)
               .WithMany()
               .HasForeignKey(e => e.LegalRegimeId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.PlanCode })
               .IsUnique()
               .HasDatabaseName("IX_BenefitPlans_TenantId_PlanCode");
    }
}
