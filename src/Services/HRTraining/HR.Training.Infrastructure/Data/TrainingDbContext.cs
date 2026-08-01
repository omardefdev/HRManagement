using HR.Training.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Training.Infrastructure.Data;

public class TrainingDbContext : TenantAwareDbContext
{
    public TrainingDbContext(DbContextOptions<TrainingDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<TrainingCourse> TrainingCourses => Set<TrainingCourse>();
    public DbSet<TrainingEnrollment> TrainingEnrollments => Set<TrainingEnrollment>();
    public DbSet<QualificationCode> QualificationCodes => Set<QualificationCode>();
    public DbSet<TrainingResult> TrainingResults => Set<TrainingResult>();
    public DbSet<MissionFile> MissionFiles => Set<MissionFile>();
    public DbSet<TrainingNomination> TrainingNominations => Set<TrainingNomination>();
    public DbSet<TrainingProvider> TrainingProviders => Set<TrainingProvider>();
    public DbSet<TrainingPlan> TrainingPlans => Set<TrainingPlan>();
    public DbSet<ELearningCourse> ELearningCourses => Set<ELearningCourse>();
    public DbSet<CourseModule> CourseModules => Set<CourseModule>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LearningEnrollment> LearningEnrollments => Set<LearningEnrollment>();
    public DbSet<LearningCertificate> LearningCertificates => Set<LearningCertificate>();
    public DbSet<TrainingChecklist> TrainingChecklists => Set<TrainingChecklist>();
    public DbSet<OrgUnitSnapshot> OrgUnitSnapshots => Set<OrgUnitSnapshot>();
    public DbSet<EmployeeOrgUnitSnapshot> EmployeeOrgUnitSnapshots => Set<EmployeeOrgUnitSnapshot>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TrainingCourse>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();

            entity.HasOne(e => e.Provider)
                  .WithMany()
                  .HasForeignKey(e => e.ProviderId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Enrollments)
                  .WithOne(e => e.Course)
                  .HasForeignKey(e => e.CourseId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Nominations)
                  .WithOne(e => e.Course)
                  .HasForeignKey(e => e.CourseId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TrainingEnrollment>(entity =>
        {
            entity.Property(e => e.CertificateNo).HasMaxLength(100);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.HasOne(e => e.Result)
                  .WithOne(e => e.Enrollment)
                  .HasForeignKey<TrainingResult>(e => e.EnrollmentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrainingChecklist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.HasIndex(e => new { e.EnrollmentId, e.Step }).IsUnique();
        });

        modelBuilder.Entity<QualificationCode>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<MissionFile>(entity =>
        {
            entity.HasIndex(e => e.EmployeeId);
        });

        modelBuilder.Entity<TrainingNomination>(entity =>
        {
            entity.HasIndex(e => new { e.EmployeeId, e.CourseId });
        });

        modelBuilder.Entity<TrainingPlan>(entity =>
        {
            entity.HasIndex(e => new { e.Year, e.OrgUnitId });
        });

        modelBuilder.Entity<ELearningCourse>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();

            entity.HasMany(e => e.Modules)
                  .WithOne(m => m.ELearningCourse)
                  .HasForeignKey(m => m.ELearningCourseId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Enrollments)
                  .WithOne(en => en.ELearningCourse)
                  .HasForeignKey(en => en.ELearningCourseId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CourseModule>(entity =>
        {
            entity.HasMany(e => e.Lessons)
                  .WithOne(l => l.CourseModule)
                  .HasForeignKey(l => l.CourseModuleId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LearningEnrollment>(entity =>
        {
            entity.HasIndex(e => new { e.ELearningCourseId, e.EmployeeId }).IsUnique();

            entity.HasOne(e => e.Certificate)
                  .WithOne(c => c.LearningEnrollment)
                  .HasForeignKey<LearningCertificate>(c => c.LearningEnrollmentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LearningCertificate>(entity =>
        {
            entity.HasIndex(e => e.CertificateNo).IsUnique();
        });

        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }
}
