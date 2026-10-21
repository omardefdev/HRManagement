using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.MasterData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.ServiceDefaults.Workflow;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

public class Est006EstablishmentJobCatalogImportTests
{
    [Fact]
    public void ParseCsvContent_reads_expected_columns()
    {
        const string csv = """
            TitleAr,TitleEn,DescriptionAr,GradeCode,JobClassificationCode,IsActive,LegacyJobCode
            مدير,Manager,,GR-05,JC-ADM,true,
            """;

        var rows = FileJobCatalogPort.ParseCsvContent(csv);

        Assert.Single(rows);
        Assert.Equal("مدير", rows[0].TitleAr);
        Assert.Equal("GR-05", rows[0].GradeCode);
        Assert.Equal("JC-ADM", rows[0].JobClassificationCode);
    }

    [Fact]
    public async Task ImportCsvAsync_creates_bulk_draft_for_valid_rows()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var service = CreateImportService(db);

        var csv = """
            TitleAr,TitleEn,DescriptionAr,GradeCode,JobClassificationCode,IsActive,LegacyJobCode
            مدير,Manager,,GR-05,JC-ADM,true,
            محلل,Analyst,,GR-05,JC-ADM,true,
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (batch, changeRequest, error) = await service.ImportCsvAsync(stream, "sample.csv", JobCatalogImportMode.Standard, Guid.NewGuid());

        Assert.Null(error);
        Assert.NotNull(batch);
        Assert.NotNull(changeRequest);
        Assert.Equal(2, batch!.RowCount);
        Assert.Equal(EstablishmentChangeType.BulkCreateJobCatalog, changeRequest!.ChangeType);
        Assert.Equal(EstablishmentChangeStatus.Draft, changeRequest.Status);
        Assert.Equal(2, await db.JobCatalogImportLines.CountAsync());
    }

    [Fact]
    public async Task ImportCsvAsync_rejects_invalid_grade_with_line_number()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var service = CreateImportService(db);

        const string csv = """
            TitleAr,TitleEn,DescriptionAr,GradeCode,JobClassificationCode,IsActive,LegacyJobCode
            مدير,Manager,,INVALID,JC-ADM,true,
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (_, changeRequest, error) = await service.ImportCsvAsync(stream, "bad.csv", JobCatalogImportMode.Standard, null);

        Assert.Null(changeRequest);
        Assert.NotNull(error);
        Assert.Contains("السطر", error, StringComparison.Ordinal);
        Assert.Contains("رمز الدرجة", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportCsvAsync_rejects_legacy_code_in_standard_mode()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var service = CreateImportService(db);

        const string csv = """
            TitleAr,TitleEn,DescriptionAr,GradeCode,JobClassificationCode,IsActive,LegacyJobCode
            مدير,Manager,,GR-05,JC-ADM,true,LEG-001
            """;

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var (_, changeRequest, error) = await service.ImportCsvAsync(stream, "legacy.csv", JobCatalogImportMode.Standard, null);

        Assert.Null(changeRequest);
        Assert.Contains("LegacyJobCode", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apply_bulk_create_generates_multiple_entries()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var applier = CreateApplier(db, ["CODE-001", "CODE-002"]);

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-006",
            ChangeType = EstablishmentChangeType.BulkCreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """
                {
                  "ImportBatchId": "00000000-0000-0000-0000-000000000001",
                  "Items": [
                    {"JobCode":"","TitleAr":"أ","GradeCode":"GR-05","OccupationalGroup":"JC-ADM","IsActive":true},
                    {"JobCode":"","TitleAr":"ب","GradeCode":"GR-05","OccupationalGroup":"JC-ADM","IsActive":true}
                  ]
                }
                """,
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.True(result.Success, result.Error);
        Assert.Equal(2, await db.JobCatalogEntries.CountAsync());
    }

    [Fact]
    public async Task Apply_bulk_create_uses_legacy_job_code_in_migration_payload()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var applier = CreateApplier(db, ["SHOULD-NOT-USE"]);

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-006B",
            ChangeType = EstablishmentChangeType.BulkCreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """
                {
                  "ImportBatchId": "00000000-0000-0000-0000-000000000002",
                  "Items": [
                    {"JobCode":"LEG-001","TitleAr":"قديم","GradeCode":"GR-05","OccupationalGroup":"JC-ADM","IsActive":true}
                  ]
                }
                """,
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.True(result.Success, result.Error);
        var entry = await db.JobCatalogEntries.SingleAsync();
        Assert.Equal("LEG-001", entry.JobCode);
    }

    private static JobCatalogImportService CreateImportService(HRCoreDbContext db)
    {
        var codeGen = new Mock<ICodeGenerator>();
        codeGen.Setup(g => g.GenerateAsync(It.IsAny<HRManagement.SharedKernel.CodeGeneration.CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HRManagement.SharedKernel.CodeGeneration.CodeGenerationResult { Code = "EST-BULK" });

        var config = new ConfigurationBuilder().Build();
        var lifecycle = new EstablishmentChangeLifecycleService(
            db,
            codeGen.Object,
            new PolicyOrchestrator(
                db,
                Mock.Of<IWorkflowStarter>(),
                new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())),
                NullLogger<PolicyOrchestrator>.Instance),
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db))),
            new EstablishmentChangeApplier(
                db,
                new HR.Core.Domain.Services.EstablishmentService(),
                Mock.Of<IFundedPositionCodeService>(),
                CreateJobCatalogCodeMock(["GEN-001"]).Object,
                new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
                new JobCatalogVersionService(db),
                new FundedPositionVersionService(db)),
            Mock.Of<IWorkflowStarter>(),
            config);

        return new JobCatalogImportService(
            db,
            new FileJobCatalogPort(),
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            lifecycle,
            config);
    }

    private static EstablishmentChangeApplier CreateApplier(HRCoreDbContext db, IReadOnlyList<string> generatedCodes)
    {
        return new EstablishmentChangeApplier(
            db,
            new HR.Core.Domain.Services.EstablishmentService(),
            Mock.Of<IFundedPositionCodeService>(),
            CreateJobCatalogCodeMock(generatedCodes).Object,
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));
    }

    private static Mock<IJobCatalogCodeService> CreateJobCatalogCodeMock(IReadOnlyList<string> codes)
    {
        var mock = new Mock<IJobCatalogCodeService>();
        var index = 0;
        mock.Setup(s => s.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => codes[Math.Min(index++, codes.Count - 1)]);
        return mock;
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
