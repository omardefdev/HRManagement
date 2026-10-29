using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.MasterData;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class Est005EstablishmentMdmConsumerIntegrationTests
{
    [Fact]
    public async Task Submit_create_job_catalog_with_valid_mdm_applies_generated_code()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);
        var lifecycle = CreateLifecycle(db);

        var request = await lifecycle.CreateDraftAsync(
            EstablishmentChangeType.CreateJobCatalog,
            new JobCatalogPayload
            {
                JobCode = string.Empty,
                TitleAr = "محلل MDM",
                GradeCode = "GR-05",
                OccupationalGroup = "JC-ADM"
            });

        var (success, error) = await EstablishmentApprovalTestHelper.SubmitAndApproveAsync(
            db,
            lifecycle,
            request.Id,
            Syr001LegislationIds.Law50_2004,
            decisionReference: "DEC-MDM",
            actorUserId: Guid.NewGuid(),
            actorUserName: "central");

        Assert.True(success, error);
        var entry = await db.JobCatalogEntries.SingleAsync();
        Assert.Equal("GR-05-JC-ADM-001", entry.JobCode);
        Assert.Equal("GR-05", entry.GradeCode);
    }

    private static EstablishmentChangeLifecycleService CreateLifecycle(HRCoreDbContext db)
    {
        var codeGen = new Mock<ICodeGenerator>();
        codeGen.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodeGenerationRequest _, CancellationToken _) => new CodeGenerationResult { Code = "EST-TEST" });

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
            .ReturnsAsync("GR-05-JC-ADM-001");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();

        var applier = new EstablishmentChangeApplier(
            db,
            new HR.Core.Domain.Services.EstablishmentService(),
            fundedCode.Object,
            jobCatalogCode.Object,
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));

        var orchestrator = new PolicyOrchestrator(
            db,
            new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance),
            new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())),
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
