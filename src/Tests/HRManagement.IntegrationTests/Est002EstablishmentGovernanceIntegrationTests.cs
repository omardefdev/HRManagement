using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.MasterData;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Est002EstablishmentGovernanceIntegrationTests
{
    [Fact]
    public async Task Submit_without_legal_source_is_rejected()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);
        var lifecycle = CreateLifecycle(db);

        var request = await lifecycle.CreateDraftAsync(
            EstablishmentChangeType.CreateJobCatalog,
            new JobCatalogPayload { JobCode = string.Empty, TitleAr = "اختبار", GradeCode = "G5", OccupationalGroup = "JC-ADM" });

        var (success, error) = await lifecycle.SubmitForApprovalAsync(
            request.Id, legalSourceId: null, decisionReference: null, actorUserId: Guid.NewGuid(), actorUserName: "test");

        Assert.False(success);
        Assert.NotNull(error);
        Assert.Equal(EstablishmentChangeStatus.Draft, (await db.EstablishmentChangeRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Submit_in_off_mode_waits_for_approval_then_applies_create_funded_position_and_logs()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);
        var org = new OrgUnit { Code = "OU-EST", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var catalog = new JobCatalogEntry { JobCode = "JC-EST", TitleAr = "منصب", GradeCode = "G5", IsActive = true };
        db.JobCatalogEntries.Add(catalog);
        await db.SaveChangesAsync();

        var lifecycle = CreateLifecycle(db);
        var request = await lifecycle.CreateDraftAsync(
            EstablishmentChangeType.CreateFundedPosition,
            new FundedPositionCreatePayload
            {
                BudgetYear = 2026,
                OrgUnitId = org.Id,
                JobCatalogEntryId = catalog.Id,
                GradeCode = "G5",
                FTE = 1.0m
            });

        var (success, error) = await EstablishmentApprovalTestHelper.SubmitAndApproveAsync(
            db,
            lifecycle,
            request.Id,
            Syr001LegislationIds.Law50_2004,
            decisionReference: "DEC-001",
            actorUserId: Guid.NewGuid(),
            actorUserName: "central");

        Assert.True(success, error);
        var updated = await db.EstablishmentChangeRequests.SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.Applied, updated.Status);
        Assert.True(await db.FundedPositions.AnyAsync());
        Assert.True(await db.EstablishmentChangeLogs.AnyAsync(l => l.ChangeRequestId == request.Id));
    }

    [Fact]
    public async Task Workflow_reject_restores_draft_without_live_changes()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);
        var lifecycle = CreateLifecycle(db);
        var handler = new EstablishmentChangeWorkflowHandler(db, lifecycle);

        var request = await lifecycle.CreateDraftAsync(
            EstablishmentChangeType.CreateJobCatalog,
            new JobCatalogPayload { JobCode = string.Empty, TitleAr = "مرفوض", GradeCode = "G5", OccupationalGroup = "JC-ADM" });
        request.Status = EstablishmentChangeStatus.PendingApproval;
        request.LegalSourceId = Syr001LegislationIds.Law50_2004;
        await db.SaveChangesAsync();

        var instance = new HR.Core.Domain.Entities.Workflow.WorkflowInstance
        {
            EntityType = "EstablishmentChangeRequest",
            EntityId = request.Id,
            InitiatedBy = Guid.NewGuid()
        };

        await handler.HandleAsync(instance, approved: false, reason: "no");

        Assert.Equal(EstablishmentChangeStatus.Draft, (await db.EstablishmentChangeRequests.SingleAsync()).Status);
        Assert.False(await db.JobCatalogEntries.AnyAsync(e => e.TitleAr == "مرفوض"));
    }

    private static EstablishmentChangeApplier CreateApplier(HRCoreDbContext db)
    {
        var fundedCode = new Mock<IFundedPositionCodeService>();
        fundedCode.Setup(s => s.CreateAsync(It.IsAny<FundedPosition>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FundedPosition draft, CancellationToken _) =>
            {
                draft.PositionCode = "POS-EST";
                draft.CostCenter = "CC-EST";
                db.FundedPositions.Add(draft);
                db.SaveChanges();
                return draft;
            });

        var jobCatalogCode = new Mock<IJobCatalogCodeService>();
        jobCatalogCode.Setup(s => s.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("JC-GEN-001");

        return new EstablishmentChangeApplier(
            db,
            new HR.Core.Domain.Services.EstablishmentService(),
            fundedCode.Object,
            jobCatalogCode.Object,
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));
    }

    private static EstablishmentChangeLifecycleService CreateLifecycle(HRCoreDbContext db)
    {
        var codeGen = new Mock<ICodeGenerator>();
        codeGen.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodeGenerationRequest _, CancellationToken _) => new CodeGenerationResult { Code = "EST-TEST" });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();

        var applier = CreateApplier(db);
        var orchestrator = new PolicyOrchestrator(
            db,
            new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance),
            new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
            NullLogger<PolicyOrchestrator>.Instance);

        return new EstablishmentChangeLifecycleService(
            db,
            codeGen.Object,
            orchestrator,
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db))),
            applier,
            new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance),
            config);
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
