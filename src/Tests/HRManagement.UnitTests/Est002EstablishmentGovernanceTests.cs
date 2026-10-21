using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.MasterData;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class Est002EstablishmentGovernanceTests
{
    [Fact]
    public void CanSubmit_only_from_draft()
    {
        Assert.True(EstablishmentChangeWorkflowRules.CanSubmit(EstablishmentChangeStatus.Draft));
        Assert.False(EstablishmentChangeWorkflowRules.CanSubmit(EstablishmentChangeStatus.PendingApproval));
    }

    [Fact]
    public void CanApply_from_approved_or_pending_when_off_mode()
    {
        Assert.True(EstablishmentChangeWorkflowRules.CanApply(EstablishmentChangeStatus.Approved));
        Assert.True(EstablishmentChangeWorkflowRules.CanApply(EstablishmentChangeStatus.PendingApproval));
        Assert.False(EstablishmentChangeWorkflowRules.CanApply(EstablishmentChangeStatus.Draft));
    }

    [Fact]
    public async Task Apply_create_job_catalog_persists_entry()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var applier = CreateApplier(db);

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-001",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"G5","OccupationalGroup":"JC-ADM","IsActive":true}""",
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.True(result.Success, result.Error);
        Assert.Equal(nameof(JobCatalogEntry), result.EntityType);
        Assert.True(await db.JobCatalogEntries.AnyAsync(e => e.TitleAr == "محلل"));
    }

    [Fact]
    public async Task Apply_freeze_occupied_position_fails()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-1", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "POS-1",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Occupied,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var applier = CreateApplier(db);
        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-002",
            ChangeType = EstablishmentChangeType.FreezeFundedPosition,
            Status = EstablishmentChangeStatus.Approved,
            TargetEntityId = position.Id,
            PayloadJson = $$"""{"FundedPositionId":"{{position.Id}}","FreezeReason":"test"}""",
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.False(result.Success);
        Assert.Equal(FundedPositionStatus.Occupied, (await db.FundedPositions.SingleAsync()).Status);
    }

    [Fact]
    public async Task Apply_deactivate_job_catalog_sets_inactive()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var entry = new JobCatalogEntry
        {
            JobCode = "JC-OLD",
            TitleAr = "قديم",
            GradeCode = "G4",
            IsActive = true
        };
        db.JobCatalogEntries.Add(entry);
        await db.SaveChangesAsync();

        var applier = CreateApplier(db);
        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-003",
            ChangeType = EstablishmentChangeType.DeactivateJobCatalog,
            Status = EstablishmentChangeStatus.Approved,
            TargetEntityId = entry.Id,
            PayloadJson = "{}",
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.True(result.Success, result.Error);
        Assert.False((await db.JobCatalogEntries.SingleAsync()).IsActive);
    }

    private static EstablishmentChangeApplier CreateApplier(HRCoreDbContext db)
    {
        var fundedCode = new Mock<IFundedPositionCodeService>();
        fundedCode.Setup(s => s.CreateAsync(It.IsAny<FundedPosition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundedPosition draft, CancellationToken _) =>
            {
                draft.PositionCode = "POS-GEN";
                draft.CostCenter = "CC-GEN";
                db.FundedPositions.Add(draft);
                db.SaveChanges();
                return draft;
            });

        var jobCatalogCode = new Mock<IJobCatalogCodeService>();
        jobCatalogCode.Setup(s => s.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JC-GEN-001");

        return new EstablishmentChangeApplier(
            db,
            new EstablishmentService(),
            fundedCode.Object,
            jobCatalogCode.Object,
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));
    }

    private static HRCoreDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new HRCoreDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
