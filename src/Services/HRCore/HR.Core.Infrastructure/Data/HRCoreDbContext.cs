using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.MasterData;
using HRManagement.SharedKernel.Signatures;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Signatures;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Data;

public class HRCoreDbContext : TenantAwareDbContext
{
    public HRCoreDbContext(DbContextOptions<HRCoreDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeCorrection> EmployeeCorrections => Set<EmployeeCorrection>();
    public DbSet<EmployeeProfile> EmployeeProfiles => Set<EmployeeProfile>();
    public DbSet<EmploymentRecord> EmploymentRecords => Set<EmploymentRecord>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<Qualification> Qualifications => Set<Qualification>();
    public DbSet<ContactInfo> ContactInfos => Set<ContactInfo>();
    public DbSet<EmploymentStatusHistory> EmploymentStatusHistories => Set<EmploymentStatusHistory>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<HRManagement.SharedKernel.Tenancy.Tenant> Tenants => Set<HRManagement.SharedKernel.Tenancy.Tenant>();
    public DbSet<TenantAssignmentRule> TenantAssignmentRules => Set<TenantAssignmentRule>();
    public DbSet<TenantQuarantineEntry> TenantQuarantineEntries => Set<TenantQuarantineEntry>();
    public DbSet<HRManagement.SharedKernel.SeparationOfDuties.SodRule> SodRules => Set<HRManagement.SharedKernel.SeparationOfDuties.SodRule>();
    public DbSet<HRManagement.SharedKernel.SeparationOfDuties.ConflictingRolePair> ConflictingRolePairs => Set<HRManagement.SharedKernel.SeparationOfDuties.ConflictingRolePair>();
    public DbSet<HRManagement.SharedKernel.SeparationOfDuties.ApprovalDelegation> ApprovalDelegations => Set<HRManagement.SharedKernel.SeparationOfDuties.ApprovalDelegation>();
    public DbSet<PerformanceEvaluation> PerformanceEvaluations => Set<PerformanceEvaluation>();
    public DbSet<ApprovedCV> ApprovedCVs => Set<ApprovedCV>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<WorkforcePlan> WorkforcePlans => Set<WorkforcePlan>();
    public DbSet<HrStatisticReport> HrStatisticReports => Set<HrStatisticReport>();
    public DbSet<DailyAttendanceRecord> DailyAttendanceRecords => Set<DailyAttendanceRecord>();
    public DbSet<FieldInspectionVisit> FieldInspectionVisits => Set<FieldInspectionVisit>();
    public DbSet<DiwanCorrespondence> DiwanCorrespondences => Set<DiwanCorrespondence>();
    public DbSet<RecruitmentApplication> RecruitmentApplications => Set<RecruitmentApplication>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalStep> ApprovalSteps => Set<ApprovalStep>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftRoster> ShiftRosters => Set<ShiftRoster>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<CareerPath> CareerPaths => Set<CareerPath>();
    public DbSet<CareerPathStep> CareerPathSteps => Set<CareerPathStep>();
    public DbSet<KeyPosition> KeyPositions => Set<KeyPosition>();
    public DbSet<SuccessionPlan> SuccessionPlans => Set<SuccessionPlan>();
    public DbSet<SuccessorCandidate> SuccessorCandidates => Set<SuccessorCandidate>();
    public DbSet<InternalVacancy> InternalVacancies => Set<InternalVacancy>();
    public DbSet<InternalApplication> InternalApplications => Set<InternalApplication>();
    public DbSet<PublicJobPosting> PublicJobPostings => Set<PublicJobPosting>();
    public DbSet<CitizenApplication> CitizenApplications => Set<CitizenApplication>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();
    public DbSet<FileReferenceOutboxMessage> FileReferenceOutbox => Set<FileReferenceOutboxMessage>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<MasterDataDuplicateCase> MasterDataDuplicateCases => Set<MasterDataDuplicateCase>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<WorkflowDelegation> WorkflowDelegations => Set<WorkflowDelegation>();
    public DbSet<SignatureRecord> SignatureRecords => Set<SignatureRecord>();
    public DbSet<ReferenceCatalogEntry> ReferenceCatalogEntries => Set<ReferenceCatalogEntry>();
    public DbSet<OrgUnitVersion> OrgUnitVersions => Set<OrgUnitVersion>();
    public DbSet<JobCatalogEntry> JobCatalogEntries => Set<JobCatalogEntry>();
    public DbSet<FundedPosition> FundedPositions => Set<FundedPosition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MasterDataDuplicateCase>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Status, e.OpenedAt });
            entity.Property(e => e.CaseType).HasMaxLength(64);
            entity.Property(e => e.NormalizedKey).HasMaxLength(64);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.NationalIdHash }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.FullNameNormalized });
            entity.ConfigureOptimisticConcurrency(this);
            entity.HasOne(e => e.PrimaryOrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.PrimaryOrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Employee>()
                  .WithMany()
                  .HasForeignKey(e => e.MergedIntoEmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.LegalHoldReason).HasMaxLength(500);
            entity.Property(e => e.FirstNameNormalized).HasMaxLength(100);
            entity.Property(e => e.MiddleNameNormalized).HasMaxLength(100);
            entity.Property(e => e.LastNameNormalized).HasMaxLength(100);
            entity.Property(e => e.FullNameNormalized).HasMaxLength(300);
        });

        modelBuilder.Entity<EmployeeCorrection>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Corrections)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Reason).HasMaxLength(1000);
            entity.HasIndex(e => new { e.EmployeeId, e.Status });
        });

        modelBuilder.Entity<EmployeeProfile>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Profiles)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmploymentRecord>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.EmploymentRecords)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmployeeDocument>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Documents)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Qualification>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Qualifications)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ContactInfo>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Contacts)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmploymentStatusHistory>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.StatusHistory)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.EmploymentRecord)
                  .WithMany()
                  .HasForeignKey(e => e.EmploymentRecordId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<OrgUnit>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Code }).IsUnique();
            entity.HasIndex(e => e.MaterializedPath);
            entity.HasIndex(e => new { e.TenantId, e.MaterializedPath });
            entity.HasOne(e => e.Parent)
                  .WithMany(e => e.Children)
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.HeadEmployee)
                  .WithMany()
                  .HasForeignKey(e => e.HeadEmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HRManagement.SharedKernel.Tenancy.Tenant>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<TenantAssignmentRule>(entity =>
        {
            entity.HasIndex(e => new { e.TargetTable, e.DiscoveryMethod, e.TargetTenantId });
        });

        modelBuilder.Entity<TenantQuarantineEntry>(entity =>
        {
            entity.HasIndex(e => new { e.ServiceName, e.TableName, e.RecordId }).IsUnique();
        });

        modelBuilder.Entity<HRManagement.SharedKernel.SeparationOfDuties.SodRule>(entity =>
        {
            entity.HasIndex(e => e.EntityType).IsUnique();
            entity.HasData(HRManagement.SharedKernel.SeparationOfDuties.SodMatrixDefaults.CreateDefaultRules());
        });

        modelBuilder.Entity<HRManagement.SharedKernel.SeparationOfDuties.ConflictingRolePair>(entity =>
        {
            entity.HasIndex(e => new { e.RoleA, e.RoleB }).IsUnique();
            entity.HasData(HRManagement.SharedKernel.SeparationOfDuties.SodMatrixDefaults.CreateDefaultConflicts());
        });

        modelBuilder.Entity<HRManagement.SharedKernel.SeparationOfDuties.ApprovalDelegation>(entity =>
        {
            entity.HasIndex(e => new { e.PrincipalId, e.DelegateId, e.ActiveFrom });
            entity.HasIndex(e => new { e.IsRevoked, e.ActiveUntil });
        });

        modelBuilder.Entity<PerformanceEvaluation>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany(emp => emp.Evaluations)
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ApprovedCV>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureMassTransitOutbox();

        modelBuilder.Entity<WorkforcePlan>(entity =>
        {
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<HrStatisticReport>(entity =>
        {
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DailyAttendanceRecord>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.EmployeeId, e.AttendanceDate }).IsUnique();
        });

        modelBuilder.Entity<DiwanCorrespondence>(entity =>
        {
            entity.HasOne(e => e.RelatedEmployee)
                  .WithMany()
                  .HasForeignKey(e => e.RelatedEmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RecruitmentApplication>(entity =>
        {
            entity.HasOne(e => e.HiredEmployee)
                  .WithMany()
                  .HasForeignKey(e => e.HiredEmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.Status);
            entity.ConfigureOptimisticConcurrency(this);
        });

        modelBuilder.Entity<ApprovalStep>(entity =>
        {
            entity.HasOne(e => e.ApprovalRequest)
                  .WithMany(r => r.Steps)
                  .HasForeignKey(e => e.ApprovalRequestId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.ApprovalRequestId, e.Level }).IsUnique();
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<ShiftRoster>(entity =>
        {
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.OrgUnitId, e.PeriodStart });
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.HasOne(e => e.ShiftRoster)
                  .WithMany(r => r.Assignments)
                  .HasForeignKey(e => e.ShiftRosterId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Shift)
                  .WithMany(s => s.Assignments)
                  .HasForeignKey(e => e.ShiftId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.EmployeeId, e.WorkDate });
        });

        modelBuilder.Entity<CareerPath>(entity =>
        {
            entity.HasMany(e => e.Steps)
                  .WithOne(s => s.CareerPath)
                  .HasForeignKey(s => s.CareerPathId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KeyPosition>(entity =>
        {
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SuccessionPlan>(entity =>
        {
            entity.HasOne(e => e.KeyPosition)
                  .WithMany()
                  .HasForeignKey(e => e.KeyPositionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SuccessorCandidate>(entity =>
        {
            entity.HasOne(e => e.SuccessionPlan)
                  .WithMany(p => p.Candidates)
                  .HasForeignKey(e => e.SuccessionPlanId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InternalVacancy>(entity =>
        {
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Applications)
                  .WithOne(a => a.InternalVacancy)
                  .HasForeignKey(a => a.InternalVacancyId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InternalApplication>(entity =>
        {
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.InternalVacancyId, e.EmployeeId }).IsUnique();
        });

        modelBuilder.Entity<PublicJobPosting>(entity =>
        {
            entity.HasMany(e => e.Applications)
                  .WithOne(a => a.PublicJobPosting)
                  .HasForeignKey(a => a.PublicJobPostingId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CitizenApplication>(entity =>
        {
            entity.HasIndex(e => e.ReferenceNumber).IsUnique();
        });

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.HasIndex(e => new { e.Code, e.Version }).IsUnique();
            entity.HasIndex(e => new { e.DomainEntityType, e.IsPublished });
            entity.Property(e => e.Code).HasMaxLength(50);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.DomainEntityType).HasMaxLength(100);
        });

        modelBuilder.Entity<WorkflowInstance>(entity =>
        {
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DefinitionId);
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<WorkflowDelegation>(entity =>
        {
            entity.HasIndex(e => new { e.PrincipalId, e.DelegateId, e.ActiveFrom });
            entity.HasIndex(e => new { e.IsRevoked, e.ActiveUntil });
        });

        modelBuilder.Entity<OrgUnitVersion>(entity =>
        {
            entity.HasIndex(e => new { e.OrgUnitId, e.ValidFrom });
            entity.HasIndex(e => new { e.TenantId, e.OrgUnitId, e.ValidFrom });
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.OperationType).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JobCatalogEntry>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.JobCode }).IsUnique();
            entity.Property(e => e.JobCode).HasMaxLength(20);
            entity.Property(e => e.TitleAr).HasMaxLength(200);
            entity.Property(e => e.TitleEn).HasMaxLength(200);
            entity.Property(e => e.DescriptionAr).HasMaxLength(2000);
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.OccupationalGroup).HasMaxLength(100);
        });

        modelBuilder.Entity<FundedPosition>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.PositionCode, e.BudgetYear }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.Property(e => e.PositionCode).HasMaxLength(30);
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CostCenter).HasMaxLength(50);
            entity.Property(e => e.FreezeReason).HasMaxLength(500);
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.JobCatalogEntry)
                  .WithMany()
                  .HasForeignKey(e => e.JobCatalogEntryId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.OccupantEmployee)
                  .WithMany()
                  .HasForeignKey(e => e.OccupantEmployeeId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.ApplyConfiguration(new Configurations.MasterData.ReferenceCatalogEntryConfiguration());

        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureFileReferenceOutbox();
        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureSignatureRecords();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
