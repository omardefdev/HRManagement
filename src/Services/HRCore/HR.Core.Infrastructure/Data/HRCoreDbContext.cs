using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Seeding;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Entities.Policy;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Benefits;
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
    public DbSet<WorkforcePlanLine> WorkforcePlanLines => Set<WorkforcePlanLine>();
    public DbSet<FinanceBudgetImportBatch> FinanceBudgetImportBatches => Set<FinanceBudgetImportBatch>();
    public DbSet<FinanceBudgetLine> FinanceBudgetLines => Set<FinanceBudgetLine>();
    public DbSet<JobCatalogImportBatch> JobCatalogImportBatches => Set<JobCatalogImportBatch>();
    public DbSet<JobCatalogImportLine> JobCatalogImportLines => Set<JobCatalogImportLine>();
    public DbSet<HrStatisticReport> HrStatisticReports => Set<HrStatisticReport>();
    public DbSet<DailyAttendanceRecord> DailyAttendanceRecords => Set<DailyAttendanceRecord>();
    public DbSet<FieldInspectionVisit> FieldInspectionVisits => Set<FieldInspectionVisit>();
    public DbSet<DiwanCorrespondence> DiwanCorrespondences => Set<DiwanCorrespondence>();
    public DbSet<RecruitmentApplication> RecruitmentApplications => Set<RecruitmentApplication>();
    public DbSet<RecruitmentApplicationEvent> RecruitmentApplicationEvents => Set<RecruitmentApplicationEvent>();
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
    public DbSet<LegislationSource> LegislationSources => Set<LegislationSource>();
    public DbSet<LegislationKeyArticle> LegislationKeyArticles => Set<LegislationKeyArticle>();
    public DbSet<LegislationInstrumentLink> LegislationInstrumentLinks => Set<LegislationInstrumentLink>();
    public DbSet<LegislationSourceAlias> LegislationSourceAliases => Set<LegislationSourceAlias>();
    public DbSet<LegalRegime> LegalRegimes => Set<LegalRegime>();
    public DbSet<BenefitPlan> BenefitPlans => Set<BenefitPlan>();
    public DbSet<ServiceRegimeException> ServiceRegimeExceptions => Set<ServiceRegimeException>();
    public DbSet<EmploymentRegimeTransition> EmploymentRegimeTransitions => Set<EmploymentRegimeTransition>();
    public DbSet<OrgUnitVersion> OrgUnitVersions => Set<OrgUnitVersion>();
    public DbSet<JobCatalogEntry> JobCatalogEntries => Set<JobCatalogEntry>();
    public DbSet<FundedPosition> FundedPositions => Set<FundedPosition>();
    public DbSet<EstablishmentChangeRequest> EstablishmentChangeRequests => Set<EstablishmentChangeRequest>();
    public DbSet<EstablishmentChangeLog> EstablishmentChangeLogs => Set<EstablishmentChangeLog>();
    public DbSet<JobCatalogVersion> JobCatalogVersions => Set<JobCatalogVersion>();
    public DbSet<FundedPositionVersion> FundedPositionVersions => Set<FundedPositionVersion>();
    public DbSet<RecruitmentRequisition> RecruitmentRequisitions => Set<RecruitmentRequisition>();
    public DbSet<OfferLetter> OfferLetters => Set<OfferLetter>();
    public DbSet<SelectionCommitteeSession> SelectionCommitteeSessions => Set<SelectionCommitteeSession>();
    public DbSet<SelectionCommitteeMember> SelectionCommitteeMembers => Set<SelectionCommitteeMember>();
    public DbSet<SelectionCommitteeMemberCoi> SelectionCommitteeMemberCois => Set<SelectionCommitteeMemberCoi>();
    public DbSet<SelectionCommitteeVote> SelectionCommitteeVotes => Set<SelectionCommitteeVote>();
    public DbSet<SelectionCommitteeDecision> SelectionCommitteeDecisions => Set<SelectionCommitteeDecision>();
    public DbSet<RecruitmentAppeal> RecruitmentAppeals => Set<RecruitmentAppeal>();
    public DbSet<RecruitmentInterviewSlot> RecruitmentInterviewSlots => Set<RecruitmentInterviewSlot>();
    public DbSet<RecruitmentInterviewPanelMember> RecruitmentInterviewPanelMembers => Set<RecruitmentInterviewPanelMember>();
    public DbSet<RecruitmentAssessmentCriteria> RecruitmentAssessmentCriteria => Set<RecruitmentAssessmentCriteria>();
    public DbSet<RecruitmentAssessmentScore> RecruitmentAssessmentScores => Set<RecruitmentAssessmentScore>();
    public DbSet<PolicyTemplate> PolicyTemplates => Set<PolicyTemplate>();
    public DbSet<PolicyTemplateVersion> PolicyTemplateVersions => Set<PolicyTemplateVersion>();
    public DbSet<PolicyRule> PolicyRules => Set<PolicyRule>();
    public DbSet<PolicyFactsSchemaEntity> PolicyFactsSchemas => Set<PolicyFactsSchemaEntity>();
    public DbSet<PolicyPublication> PolicyPublications => Set<PolicyPublication>();
    public DbSet<PolicySimulationRun> PolicySimulationRuns => Set<PolicySimulationRun>();
    public DbSet<PolicyEnforcementLedger> PolicyEnforcementLedgers => Set<PolicyEnforcementLedger>();
    public DbSet<PolicyWorkflowBinding> PolicyWorkflowBindings => Set<PolicyWorkflowBinding>();
    public DbSet<CodePolicyTemplate> CodePolicyTemplates => Set<CodePolicyTemplate>();
    public DbSet<CodePolicyVersion> CodePolicyVersions => Set<CodePolicyVersion>();
    public DbSet<CodePolicyRule> CodePolicyRules => Set<CodePolicyRule>();
    public DbSet<CodeSequenceCounter> CodeSequenceCounters => Set<CodeSequenceCounter>();
    public DbSet<CodeIssuance> CodeIssuances => Set<CodeIssuance>();
    public DbSet<SeedPackApplication> SeedPackApplications => Set<SeedPackApplication>();

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
            entity.HasOne(e => e.LegalRegime)
                  .WithMany()
                  .HasForeignKey(e => e.LegalRegimeId)
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
            entity.Property(e => e.LegalSourceId).HasMaxLength(100);
            entity.HasOne(e => e.OrgUnit)
                  .WithMany()
                  .HasForeignKey(e => e.OrgUnitId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkforcePlanLine>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.WorkforcePlanId });
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.HasOne(e => e.WorkforcePlan)
                  .WithMany(p => p.Lines)
                  .HasForeignKey(e => e.WorkforcePlanId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.JobCatalogEntry)
                  .WithMany()
                  .HasForeignKey(e => e.JobCatalogEntryId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<FinanceBudgetImportBatch>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.FiscalYear, e.ImportedAt });
            entity.Property(e => e.SourceFileName).HasMaxLength(260);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ErrorSummary).HasMaxLength(2000);
        });

        modelBuilder.Entity<FinanceBudgetLine>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.BatchId, e.CostCenter });
            entity.Property(e => e.CostCenter).HasMaxLength(50);
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.ExternalReference).HasMaxLength(100);
            entity.HasOne(e => e.Batch)
                  .WithMany(b => b.Lines)
                  .HasForeignKey(e => e.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobCatalogImportBatch>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.ImportedAt });
            entity.Property(e => e.SourceFileName).HasMaxLength(260);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ImportMode).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ErrorSummary).HasMaxLength(2000);
            entity.HasOne(e => e.EstablishmentChangeRequest)
                  .WithMany()
                  .HasForeignKey(e => e.EstablishmentChangeRequestId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<JobCatalogImportLine>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.BatchId, e.LineNumber });
            entity.Property(e => e.TitleAr).HasMaxLength(200);
            entity.Property(e => e.TitleEn).HasMaxLength(200);
            entity.Property(e => e.DescriptionAr).HasMaxLength(2000);
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.JobClassificationCode).HasMaxLength(100);
            entity.Property(e => e.LegacyJobCode).HasMaxLength(20);
            entity.Property(e => e.ValidationError).HasMaxLength(500);
            entity.HasOne(e => e.Batch)
                  .WithMany(b => b.Lines)
                  .HasForeignKey(e => e.BatchId)
                  .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasOne(e => e.Requisition)
                  .WithMany()
                  .HasForeignKey(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CriteriaVersion)
                  .WithMany()
                  .HasForeignKey(e => e.CriteriaVersionId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ActiveOfferLetter)
                  .WithMany()
                  .HasForeignKey(e => e.ActiveOfferLetterId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId });
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId, e.Rank })
                  .IsUnique()
                  .HasFilter("\"Rank\" IS NOT NULL AND \"RequisitionId\" IS NOT NULL");
            entity.Property(e => e.NationalIdHash).HasMaxLength(64);
            entity.Property(e => e.AppealStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.RejectionKind).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(e => e.SourceInternalApplication)
                  .WithMany()
                  .HasForeignKey(e => e.SourceInternalApplicationId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.IntakeChannel).HasConversion<string>().HasMaxLength(20);
        });

        // HCM-030 (B1): append-only application event log (no PII).
        modelBuilder.Entity<RecruitmentApplicationEvent>(entity =>
        {
            entity.HasOne<RecruitmentApplication>()
                  .WithMany()
                  .HasForeignKey(e => e.RecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.EventType).HasMaxLength(40);
            entity.HasIndex(e => new { e.TenantId, e.RecruitmentApplicationId, e.OccurredAt });
            entity.HasIndex(e => e.OccurredAt)
                  .HasFilter("\"NotificationsPlannedAt\" IS NULL")
                  .HasDatabaseName("IX_RecruitmentApplicationEvents_NotificationsPending");
        });

        modelBuilder.Entity<RecruitmentAssessmentCriteria>(entity =>
        {
            entity.HasOne(e => e.Requisition)
                  .WithMany()
                  .HasForeignKey(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId })
                  .IsUnique()
                  .HasFilter("\"IsActive\" = true")
                  .HasDatabaseName("IX_RecruitmentAssessmentCriteria_Tenant_Requisition_Active");
        });

        modelBuilder.Entity<RecruitmentAssessmentScore>(entity =>
        {
            entity.HasOne(e => e.RecruitmentApplication)
                  .WithMany()
                  .HasForeignKey(e => e.RecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CriteriaVersion)
                  .WithMany()
                  .HasForeignKey(e => e.CriteriaVersionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.Score).HasPrecision(7, 2);
            entity.HasIndex(e => new { e.TenantId, e.RecruitmentApplicationId, e.AssessorUserId, e.ItemKey }).IsUnique();
        });

        modelBuilder.Entity<OfferLetter>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.OfferNumber }).IsUnique();
            entity.HasOne(e => e.RecruitmentApplication)
                  .WithMany(a => a.OfferLetters)
                  .HasForeignKey(e => e.RecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.FundedPosition)
                  .WithMany()
                  .HasForeignKey(e => e.FundedPositionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.LegalSource)
                  .WithMany()
                  .HasForeignKey(e => e.LegalSourceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.RecruitmentApplicationId, e.Status })
                  .HasFilter("\"Status\" IN (2, 3)")
                  .IsUnique();
            entity.Property(e => e.GradeMinSalary).HasPrecision(18, 2);
            entity.Property(e => e.GradeMaxSalary).HasPrecision(18, 2);
            // HCM-029 (D2): hex SHA-256 → char(64).
            entity.Property(e => e.DocumentSha256).HasMaxLength(64).IsFixedLength();
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
            entity.HasOne(e => e.Requisition)
                  .WithOne(r => r.InternalVacancy)
                  .HasForeignKey<InternalVacancy>(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId })
                  .IsUnique()
                  .HasFilter("\"RequisitionId\" IS NOT NULL");
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
            entity.HasOne(e => e.Requisition)
                  .WithMany()
                  .HasForeignKey(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PromotedRecruitmentApplication)
                  .WithMany()
                  .HasForeignKey(e => e.PromotedRecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.InternalVacancyId, e.EmployeeId }).IsUnique();
            // HCM-030 (A6): one channel row per recruitment application.
            entity.HasIndex(e => e.PromotedRecruitmentApplicationId)
                  .IsUnique()
                  .HasFilter("\"PromotedRecruitmentApplicationId\" IS NOT NULL");
            entity.HasIndex(e => new { e.TenantId, e.EmployeeId, e.RequisitionId })
                  .IsUnique()
                  .HasFilter("\"RequisitionId\" IS NOT NULL");
        });

        modelBuilder.Entity<PublicJobPosting>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId })
                  .IsUnique()
                  .HasFilter("\"RequisitionId\" IS NOT NULL");
            entity.HasOne(e => e.Requisition)
                  .WithOne(r => r.PublicJobPosting)
                  .HasForeignKey<PublicJobPosting>(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Applications)
                  .WithOne(a => a.PublicJobPosting)
                  .HasForeignKey(a => a.PublicJobPostingId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CitizenApplication>(entity =>
        {
            entity.HasIndex(e => e.ReferenceNumber).IsUnique();
            // HCM-030 (A6): one channel row per recruitment application.
            entity.HasIndex(e => e.PromotedRecruitmentApplicationId)
                  .IsUnique()
                  .HasFilter("\"PromotedRecruitmentApplicationId\" IS NOT NULL");
            entity.HasOne(e => e.Requisition)
                  .WithMany()
                  .HasForeignKey(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.PromotedRecruitmentApplication)
                  .WithMany()
                  .HasForeignKey(e => e.PromotedRecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId });
            entity.HasIndex(e => new { e.TenantId, e.NationalId, e.RequisitionId })
                  .IsUnique()
                  .HasFilter("\"RequisitionId\" IS NOT NULL");
            entity.HasIndex(e => new { e.TenantId, e.NationalIdHash, e.RequisitionId })
                  .IsUnique()
                  .HasFilter("\"RequisitionId\" IS NOT NULL AND \"NationalIdHash\" IS NOT NULL");
            entity.Property(e => e.NationalIdHash).HasMaxLength(64);
        });

        modelBuilder.Entity<SelectionCommitteeSession>(entity =>
        {
            entity.HasOne(e => e.Requisition)
                  .WithMany()
                  .HasForeignKey(e => e.RequisitionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TenantId, e.RequisitionId, e.CommitteeDate });
        });

        modelBuilder.Entity<SelectionCommitteeMember>(entity =>
        {
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Members)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.TenantId, e.SessionId, e.EmployeeId }).IsUnique();
        });

        // HCM-028: per-candidate conflicts, votes and decisions (enums stored as int, like the application status).
        modelBuilder.Entity<SelectionCommitteeMemberCoi>(entity =>
        {
            entity.HasOne(e => e.Member)
                  .WithMany(m => m.CoiDeclarations)
                  .HasForeignKey(e => e.MemberId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Application)
                  .WithMany()
                  .HasForeignKey(e => e.ApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.MemberId, e.ApplicationId }).IsUnique();
        });

        modelBuilder.Entity<SelectionCommitteeVote>(entity =>
        {
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Votes)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Member)
                  .WithMany()
                  .HasForeignKey(e => e.MemberId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Application)
                  .WithMany()
                  .HasForeignKey(e => e.ApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Comment).HasMaxLength(1000);
            entity.HasIndex(e => new { e.MemberId, e.ApplicationId }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.SessionId });
        });

        modelBuilder.Entity<SelectionCommitteeDecision>(entity =>
        {
            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Decisions)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Application)
                  .WithMany()
                  .HasForeignKey(e => e.ApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.Rationale).HasMaxLength(2000);
            entity.HasIndex(e => new { e.SessionId, e.ApplicationId }).IsUnique();
        });

        modelBuilder.Entity<RecruitmentAppeal>(entity =>
        {
            entity.HasOne(e => e.RecruitmentApplication)
                  .WithMany()
                  .HasForeignKey(e => e.RecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => new { e.TenantId, e.RecruitmentApplicationId, e.Status });
            entity.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<RecruitmentInterviewSlot>(entity =>
        {
            entity.HasOne(e => e.RecruitmentApplication)
                  .WithMany()
                  .HasForeignKey(e => e.RecruitmentApplicationId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CitizenApplication)
                  .WithMany()
                  .HasForeignKey(e => e.CitizenApplicationId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.TenantId, e.RecruitmentApplicationId, e.ScheduledAt });
            // NO ACTION (not RESTRICT): still blocks orphaning a chain, but is checked at statement end,
            // so cascading an application delete across a reschedule chain succeeds.
            entity.HasOne(e => e.RescheduledFromSlot)
                  .WithMany()
                  .HasForeignKey(e => e.RescheduledFromSlotId)
                  .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RecruitmentInterviewPanelMember>(entity =>
        {
            entity.HasOne(e => e.Slot)
                  .WithMany(s => s.PanelMembers)
                  .HasForeignKey(e => e.SlotId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Employee)
                  .WithMany()
                  .HasForeignKey(e => e.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => new { e.SlotId, e.EmployeeId }).IsUnique();
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

        modelBuilder.Entity<PolicyTemplate>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Domain, e.Scope });
            entity.Property(e => e.Domain).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.SubjectType).HasMaxLength(100);
            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<PolicyTemplateVersion>(entity =>
        {
            entity.HasIndex(e => new { e.PolicyTemplateId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.EffectiveFrom });
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(e => e.PolicyTemplate)
                  .WithMany(t => t.Versions)
                  .HasForeignKey(e => e.PolicyTemplateId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PolicyRule>(entity =>
        {
            entity.HasOne(e => e.PolicyTemplateVersion)
                  .WithMany(v => v.Rules)
                  .HasForeignKey(e => e.PolicyTemplateVersionId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.Property(e => e.RuleKey).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<PolicyFactsSchemaEntity>(entity =>
        {
            entity.HasIndex(e => e.Domain).IsUnique();
            entity.Property(e => e.Domain).HasMaxLength(100);
            entity.Property(e => e.SubjectType).HasMaxLength(100);
        });

        modelBuilder.Entity<PolicyEnforcementLedger>(entity =>
        {
            entity.HasIndex(e => new { e.Domain, e.OccurredAt });
            entity.HasIndex(e => e.CorrelationId);
            entity.Property(e => e.Domain).HasMaxLength(100);
            entity.Property(e => e.Action).HasMaxLength(64);
            entity.Property(e => e.CorrelationId).HasMaxLength(128);
            entity.Property(e => e.FactsHash).HasMaxLength(64);
            entity.Property(e => e.Mode).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<PolicyWorkflowBinding>(entity =>
        {
            entity.HasIndex(e => new { e.Domain, e.WorkflowCode }).IsUnique();
            entity.Property(e => e.Domain).HasMaxLength(100);
            entity.Property(e => e.WorkflowCode).HasMaxLength(50);
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

        modelBuilder.Entity<EstablishmentChangeRequest>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RequestNumber }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => e.TargetEntityId);
            entity.Property(e => e.RequestNumber).HasMaxLength(50);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.LegalSourceId).HasMaxLength(100);
            entity.Property(e => e.DecisionReference).HasMaxLength(100);
            entity.Property(e => e.JustificationNotes).HasMaxLength(500);
            entity.Property(e => e.ChangeType).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(e => e.LegalSource)
                  .WithMany()
                  .HasForeignKey(e => e.LegalSourceId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EstablishmentChangeLog>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.EntityType, e.EntityId });
            entity.HasIndex(e => e.ChangeRequestId);
            entity.Property(e => e.EntityType).HasMaxLength(64);
            entity.Property(e => e.LegalSourceId).HasMaxLength(100);
            entity.Property(e => e.DecisionReference).HasMaxLength(100);
            entity.Property(e => e.ChangeType).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(e => e.ChangeRequest)
                  .WithMany()
                  .HasForeignKey(e => e.ChangeRequestId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<JobCatalogVersion>(entity =>
        {
            entity.HasIndex(e => new { e.JobCatalogEntryId, e.ValidFrom });
            entity.HasIndex(e => new { e.TenantId, e.JobCatalogEntryId, e.ValidFrom });
            entity.Property(e => e.JobCode).HasMaxLength(20);
            entity.Property(e => e.TitleAr).HasMaxLength(200);
            entity.Property(e => e.TitleEn).HasMaxLength(200);
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.OccupationalGroup).HasMaxLength(100);
            entity.Property(e => e.OperationType).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.LegalSourceId).HasMaxLength(100);
            entity.Property(e => e.DecisionReference).HasMaxLength(100);
            entity.HasOne(e => e.JobCatalogEntry)
                  .WithMany()
                  .HasForeignKey(e => e.JobCatalogEntryId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FundedPositionVersion>(entity =>
        {
            entity.HasIndex(e => new { e.FundedPositionId, e.ValidFrom });
            entity.HasIndex(e => new { e.TenantId, e.FundedPositionId, e.ValidFrom });
            entity.HasIndex(e => new { e.TenantId, e.OrgUnitId, e.ValidFrom });
            entity.Property(e => e.PositionCode).HasMaxLength(30);
            entity.Property(e => e.GradeCode).HasMaxLength(20);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.CostCenter).HasMaxLength(50);
            entity.Property(e => e.FreezeReason).HasMaxLength(500);
            entity.Property(e => e.OperationType).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.LegalSourceId).HasMaxLength(100);
            entity.Property(e => e.DecisionReference).HasMaxLength(100);
            entity.HasOne(e => e.FundedPosition)
                  .WithMany()
                  .HasForeignKey(e => e.FundedPositionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RecruitmentRequisition>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.RequisitionNumber }).IsUnique();
            entity.HasIndex(e => new { e.TenantId, e.Status });
            entity.HasIndex(e => e.FundedPositionId);
            // HCM-026 (G-11): at most one blocking requisition per funded position (Status is stored as text).
            entity.HasIndex(e => new { e.TenantId, e.FundedPositionId })
                  .IsUnique()
                  .HasFilter("\"Status\" IN ('Draft','PendingApproval','Approved','Published')")
                  .HasDatabaseName("IX_RecruitmentRequisitions_Tenant_Position_Blocking");
            entity.Property(e => e.RequisitionNumber).HasMaxLength(50);
            entity.Property(e => e.JobTitle).HasMaxLength(200);
            entity.Property(e => e.JustificationNotes).HasMaxLength(500);
            entity.Property(e => e.LegalSourceId).HasMaxLength(100);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.ToTable(t => t.HasCheckConstraint(
                "CK_RecruitmentRequisitions_VacancyCount",
                "\"VacancyCount\" >= 0 AND \"VacancyCount\" <= 1"));
            entity.HasOne(e => e.FundedPosition)
                  .WithMany()
                  .HasForeignKey(e => e.FundedPositionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.LegalSource)
                  .WithMany()
                  .HasForeignKey(e => e.LegalSourceId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.PublishingChannel).HasConversion<string>().HasMaxLength(32);
        });

        modelBuilder.Entity<CodePolicyTemplate>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.EntityKind, e.Scope });
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.EntityKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Scope).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.PolicyKey).HasMaxLength(100);
        });

        modelBuilder.Entity<CodePolicyVersion>(entity =>
        {
            entity.HasIndex(e => new { e.CodePolicyTemplateId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => new { e.Status, e.EffectiveFrom });
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.SequenceReset).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.FallbackPrefix).HasMaxLength(20);
            entity.HasOne(e => e.Template)
                  .WithMany(t => t.Versions)
                  .HasForeignKey(e => e.CodePolicyTemplateId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CodePolicyRule>(entity =>
        {
            entity.HasOne(e => e.Template)
                  .WithMany(t => t.Rules)
                  .HasForeignKey(e => e.CodePolicyTemplateId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Version)
                  .WithMany()
                  .HasForeignKey(e => e.CodePolicyVersionId)
                  .OnDelete(DeleteBehavior.Restrict);
            entity.Property(e => e.RuleKey).HasMaxLength(100);
            entity.Property(e => e.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<CodeSequenceCounter>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.EntityKind, e.ScopeKey, e.Year }).IsUnique();
            entity.Property(e => e.EntityKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.ScopeKey).HasMaxLength(256);
        });

        modelBuilder.Entity<CodeIssuance>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.EntityKind, e.Code }).IsUnique();
            entity.HasIndex(e => e.IssuedAt);
            entity.Property(e => e.EntityKind).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Code).HasMaxLength(128);
            entity.Property(e => e.SequenceScope).HasMaxLength(256);
        });

        modelBuilder.ApplyConfiguration(new Configurations.MasterData.ReferenceCatalogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.LegislationSourceConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.LegislationKeyArticleConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.LegislationInstrumentLinkConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.LegislationSourceAliasConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.LegalRegimeConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.BenefitPlanConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.ServiceRegimeExceptionConfiguration());
        modelBuilder.ApplyConfiguration(new Configurations.Legal.EmploymentRegimeTransitionConfiguration());

        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureFileReferenceOutbox();
        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureSignatureRecords();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    /// <summary>
    /// HCM-030 (B1): while set, saves record no <see cref="RecruitmentApplicationEvent"/> rows. For bulk data fixes and
    /// historic links only (the SQL backfill records none either) — candidates are never notified of suppressed changes.
    /// </summary>
    public bool ApplicationEventsSuppressed { get; private set; }

    /// <summary>Suppresses application event recording on this context until the returned scope is disposed.</summary>
    public IDisposable SuppressApplicationEvents()
    {
        var previous = ApplicationEventsSuppressed;
        ApplicationEventsSuppressed = true;
        return new SuppressionScope(this, previous);
    }

    private sealed class SuppressionScope(HRCoreDbContext db, bool previous) : IDisposable
    {
        public void Dispose() => db.ApplicationEventsSuppressed = previous;
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
