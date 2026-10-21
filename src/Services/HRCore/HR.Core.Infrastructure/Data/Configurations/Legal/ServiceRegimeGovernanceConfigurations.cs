using HR.Core.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Core.Infrastructure.Data.Configurations.Legal;

public sealed class ServiceRegimeExceptionConfiguration : IEntityTypeConfiguration<ServiceRegimeException>
{
    public void Configure(EntityTypeBuilder<ServiceRegimeException> builder)
    {
        builder.ToTable("ServiceRegimeExceptions");

        builder.Property(e => e.ExceptionType).HasConversion<int>();
        builder.Property(e => e.Status).HasConversion<int>();
        builder.Property(e => e.SourceContext).HasConversion<int>();
        builder.Property(e => e.LegalSourceId).HasMaxLength(100);
        builder.Property(e => e.AttemptedRegimeId).HasMaxLength(50);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Resolution).HasMaxLength(500);
        builder.Property(e => e.ResolvedRegimeId).HasMaxLength(50);
        builder.Property(e => e.ConflictNotes).HasMaxLength(1000);
        builder.Property(e => e.LegalDecisionId).HasMaxLength(64);
        builder.Property(e => e.ConflictingEntitlementsJson).HasMaxLength(2000);

        builder.HasOne(e => e.Employee)
               .WithMany()
               .HasForeignKey(e => e.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EmploymentRecord)
               .WithMany()
               .HasForeignKey(e => e.EmploymentRecordId)
               .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.ExceptionType, e.Status })
               .HasDatabaseName("IX_ServiceRegimeExceptions_Tenant_Employee_Type_Status");

        builder.HasIndex(e => new { e.TenantId, e.Status })
               .HasDatabaseName("IX_ServiceRegimeExceptions_Tenant_Status");
    }
}

public sealed class EmploymentRegimeTransitionConfiguration : IEntityTypeConfiguration<EmploymentRegimeTransition>
{
    public void Configure(EntityTypeBuilder<EmploymentRegimeTransition> builder)
    {
        builder.ToTable("EmploymentRegimeTransitions");

        builder.Property(e => e.FromRegimeId).HasMaxLength(50);
        builder.Property(e => e.ToRegimeId).HasMaxLength(50).IsRequired();
        builder.Property(e => e.TransitionDecisionRef).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ConditionCode).HasMaxLength(100);
        builder.Property(e => e.ConflictNotes).HasMaxLength(1000);

        builder.HasOne(e => e.Employee)
               .WithMany()
               .HasForeignKey(e => e.EmployeeId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.EmploymentRecord)
               .WithMany()
               .HasForeignKey(e => e.EmploymentRecordId)
               .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.EffectiveFrom })
               .HasDatabaseName("IX_EmploymentRegimeTransitions_Tenant_Employee_EffectiveFrom");
    }
}
