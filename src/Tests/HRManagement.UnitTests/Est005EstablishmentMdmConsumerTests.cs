using HR.Core.Domain.Entities;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.MasterData;
using HRManagement.SharedKernel.MasterData;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class Est005EstablishmentMdmConsumerTests
{
    [Fact]
    public async Task ValidateGradeCode_rejects_unknown_code()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var validator = new EstablishmentReferenceValidator(new ReferenceDataProvider(db));

        var error = await validator.ValidateGradeCodeAsync("INVALID");

        Assert.Equal("رمز الدرجة غير موجود أو غير نشط.", error);
    }

    [Fact]
    public async Task ValidateGradeCode_accepts_seeded_grade()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var validator = new EstablishmentReferenceValidator(new ReferenceDataProvider(db));

        var error = await validator.ValidateGradeCodeAsync("GR-05");

        Assert.Null(error);
    }

    [Fact]
    public async Task Apply_create_job_catalog_generates_job_code_when_missing()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var applier = CreateApplier(db, "GR-05-JC-ADM-001");

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-005",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"GR-05","OccupationalGroup":"JC-ADM","IsActive":true}""",
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.True(result.Success, result.Error);
        var entry = await db.JobCatalogEntries.SingleAsync();
        Assert.Equal("GR-05-JC-ADM-001", entry.JobCode);
        Assert.Equal("GR-05", entry.GradeCode);
        Assert.Equal("JC-ADM", entry.OccupationalGroup);
    }

    [Fact]
    public async Task Apply_create_job_catalog_rejects_invalid_grade()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var applier = CreateApplier(db, "SHOULD-NOT-RUN");

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-005B",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"INVALID","OccupationalGroup":"JC-ADM","IsActive":true}""",
            LegalSourceId = HRManagement.SharedKernel.Legal.Syr001LegislationIds.Law50_2004
        };

        var result = await applier.ApplyAsync(request);
        Assert.False(result.Success);
        Assert.Contains("رمز الدرجة", result.Error, StringComparison.Ordinal);
        Assert.False(await db.JobCatalogEntries.AnyAsync());
    }

    private static EstablishmentChangeApplier CreateApplier(HRCoreDbContext db, string generatedCode)
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
            .ReturnsAsync(generatedCode);

        return new EstablishmentChangeApplier(
            db,
            new HR.Core.Domain.Services.EstablishmentService(),
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
