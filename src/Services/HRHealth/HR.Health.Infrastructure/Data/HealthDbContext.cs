using HR.Health.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HR.Health.Infrastructure.Data;

public class HealthDbContext : TenantAwareDbContext
{
    public HealthDbContext(DbContextOptions<HealthDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalCommitteeReport> MedicalCommitteeReports => Set<MedicalCommitteeReport>();
    public DbSet<PeriodicMedicalCheck> PeriodicMedicalChecks => Set<PeriodicMedicalCheck>();
    public DbSet<InsuranceCard> InsuranceCards => Set<InsuranceCard>();
    public DbSet<HealthStatusEntry> HealthStatusEntries => Set<HealthStatusEntry>();
    public DbSet<MedicalRecommendation> MedicalRecommendations => Set<MedicalRecommendation>();
    public DbSet<HealthRetirementRecord> HealthRetirementRecords => Set<HealthRetirementRecord>();
    public DbSet<InsuranceContract> InsuranceContracts => Set<InsuranceContract>();
    public DbSet<WorkplaceAccident> WorkplaceAccidents => Set<WorkplaceAccident>();
    public DbSet<SafetyInspection> SafetyInspections => Set<SafetyInspection>();
    public DbSet<FirstAidTrainingRecord> FirstAidTrainingRecords => Set<FirstAidTrainingRecord>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<OrgUnitSnapshot> OrgUnitSnapshots => Set<OrgUnitSnapshot>();
    public DbSet<EmployeeOrgUnitSnapshot> EmployeeOrgUnitSnapshots => Set<EmployeeOrgUnitSnapshot>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureIntegrationMessageReceipts();

        modelBuilder.Entity<MedicalRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ConsentPurpose).HasMaxLength(256);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);

            entity.HasMany(e => e.CommitteeReports)
                  .WithOne(r => r.MedicalRecord)
                  .HasForeignKey(r => r.MedicalRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MedicalCommitteeReport>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.HasMany(e => e.Recommendations)
                  .WithOne(r => r.Report)
                  .HasForeignKey(r => r.ReportId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PeriodicMedicalCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<InsuranceCard>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);

            entity.HasOne(e => e.InsuranceContract)
                  .WithMany()
                  .HasForeignKey(e => e.InsuranceContractId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HealthStatusEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<MedicalRecommendation>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<HealthRetirementRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<InsuranceContract>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<WorkplaceAccident>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => e.AccidentDate);
        });

        modelBuilder.Entity<SafetyInspection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.InspectionDate);
        });

        modelBuilder.Entity<FirstAidTrainingRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EmployeeId);
        });

        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = DateTime.UtcNow;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
