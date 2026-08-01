using Compliance.Domain.Entities;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Compliance.Infrastructure.Data;

public class ComplianceDbContext : TenantAwareDbContext
{
    public ComplianceDbContext(DbContextOptions<ComplianceDbContext> options, ITenantContext? tenantContext = null)
        : base(options, tenantContext) { }

    public DbSet<Complaint> Complaints => Set<Complaint>();
    public DbSet<AuditFinding> AuditFindings => Set<AuditFinding>();
    public DbSet<LegalCase> LegalCases => Set<LegalCase>();
    public DbSet<ComplianceRecommendation> ComplianceRecommendations => Set<ComplianceRecommendation>();
    public DbSet<DisciplinaryAction> DisciplinaryActions => Set<DisciplinaryAction>();
    public DbSet<LegalReferral> LegalReferrals => Set<LegalReferral>();
    public DbSet<ComplaintFollowUp> ComplaintFollowUps => Set<ComplaintFollowUp>();
    public DbSet<IntegrationMessageReceipt> IntegrationMessageReceipts => Set<IntegrationMessageReceipt>();
    public DbSet<HRManagement.SharedKernel.Entities.EntityAuditLog> AuditLogs => Set<HRManagement.SharedKernel.Entities.EntityAuditLog>();
    public DbSet<ExportJob> ExportJobs => Set<ExportJob>();
    public DbSet<DsarRequest> DsarRequests => Set<DsarRequest>();
    public DbSet<InvestigationChecklist> InvestigationChecklists => Set<InvestigationChecklist>();
    public DbSet<UnionEngagement> UnionEngagements => Set<UnionEngagement>();
    public DbSet<CollectiveGrievance> CollectiveGrievances => Set<CollectiveGrievance>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TRN-002
        modelBuilder.Entity<UnionEngagement>(e =>
        {
            e.ToTable("UnionEngagements");
            e.HasIndex(x => new { x.TenantId, x.WaveId });
            e.Property(x => x.WaveId).HasMaxLength(64);
            e.Property(x => x.ResolutionStatus).HasMaxLength(32);
        });
        modelBuilder.Entity<CollectiveGrievance>(e =>
        {
            e.ToTable("CollectiveGrievances");
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(32);
        });

        modelBuilder.Entity<DsarRequest>(entity =>
        {
            entity.HasIndex(e => new { e.TenantId, e.Status, e.RequestedAt });
            entity.Property(e => e.RequestType).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.DiscoveryPackageJson).HasColumnType("text");
            entity.Property(e => e.SubjectNationalIdHash).HasMaxLength(64);
            entity.Property(e => e.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<ComplaintFollowUp>(entity =>
        {
            entity.HasOne(e => e.Complaint)
                  .WithMany(c => c.FollowUps)
                  .HasForeignKey(e => e.ComplaintId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ComplianceRecommendation>(entity =>
        {
            entity.HasOne(e => e.Finding)
                  .WithMany(f => f.Recommendations)
                  .HasForeignKey(e => e.FindingId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .IsRequired(false);

            entity.HasOne(e => e.Complaint)
                  .WithMany()
                  .HasForeignKey(e => e.ComplaintId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<LegalReferral>(entity =>
        {
            entity.HasOne(e => e.Complaint)
                  .WithMany()
                  .HasForeignKey(e => e.ComplaintId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DisciplinaryAction>(entity =>
        {
            entity.HasOne(e => e.Complaint)
                  .WithMany()
                  .HasForeignKey(e => e.ComplaintId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Complaint>(entity =>
        {
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
        });

        modelBuilder.Entity<InvestigationChecklist>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OwnerRole).HasMaxLength(64);
            entity.Property(e => e.EvidenceRef).HasMaxLength(256);
            entity.HasIndex(e => new { e.ComplaintId, e.Step }).IsUnique();
        });

        modelBuilder.ConfigureMassTransitOutbox();
        modelBuilder.ConfigureIntegrationMessageReceipts();
        modelBuilder.ConfigureExportJobs();
        modelBuilder.ConfigureUtcDateTimeProperties();
        modelBuilder.ConfigureTenantQueryFilters(this);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UtcDateTimeNormalization.Apply(this);

        foreach (var entry in ChangeTracker.Entries<HRManagement.SharedKernel.Entities.BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
