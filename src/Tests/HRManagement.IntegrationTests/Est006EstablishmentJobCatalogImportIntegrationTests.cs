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
public class Est006EstablishmentJobCatalogImportIntegrationTests
{
    [Fact]
    public async Task Import_then_submit_applies_all_catalog_rows()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);

        var importService = CreateImportService(db);
        var lifecycle = CreateLifecycle(db);

        const string csv = """
            TitleAr,TitleEn,DescriptionAr,GradeCode,JobClassificationCode,IsActive,LegacyJobCode
            مدير,Manager,,GR-05,JC-ADM,true,
            محلل,Analyst,,GR-05,JC-ADM,true,
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (_, changeRequest, error) = await importService.ImportCsvAsync(stream, "integration.csv", JobCatalogImportMode.Standard, Guid.NewGuid());
        Assert.Null(error);
        Assert.NotNull(changeRequest);

        var (success, submitError) = await EstablishmentApprovalTestHelper.SubmitAndApproveAsync(
            db,
            lifecycle,
            changeRequest!.Id,
            Syr001LegislationIds.Law50_2004,
            decisionReference: "DEC-BULK",
            actorUserId: Guid.NewGuid(),
            actorUserName: "central");

        Assert.True(success, submitError);
        Assert.Equal(2, await db.JobCatalogEntries.CountAsync());
        Assert.Contains(await db.JobCatalogEntries.Select(e => e.JobCode).ToListAsync(), c => c == "GR-05-JC-ADM-001");
        Assert.Contains(await db.JobCatalogEntries.Select(e => e.JobCode).ToListAsync(), c => c == "GR-05-JC-ADM-002");
    }

    private static JobCatalogImportService CreateImportService(HRCoreDbContext db)
    {
        var lifecycle = CreateLifecycle(db);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();

        return new JobCatalogImportService(
            db,
            new FileJobCatalogPort(),
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            lifecycle,
            config);
    }

    private static EstablishmentChangeLifecycleService CreateLifecycle(HRCoreDbContext db)
    {
        var codeGen = new Mock<ICodeGenerator>();
        codeGen.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CodeGenerationRequest _, CancellationToken _) => new CodeGenerationResult { Code = "EST-BULK" });

        var sequence = 0;
        var jobCatalogCode = new Mock<IJobCatalogCodeService>();
        jobCatalogCode.Setup(s => s.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => $"GR-05-JC-ADM-{++sequence:000}");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();

        var applier = new EstablishmentChangeApplier(
            db,
            new HR.Core.Domain.Services.EstablishmentService(),
            Mock.Of<IFundedPositionCodeService>(),
            jobCatalogCode.Object,
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));

        return new EstablishmentChangeLifecycleService(
            db,
            codeGen.Object,
            new PolicyOrchestrator(
                db,
                new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance),
                new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())),
                NullLogger<PolicyOrchestrator>.Instance),
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
