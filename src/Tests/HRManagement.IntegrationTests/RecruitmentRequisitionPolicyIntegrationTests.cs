using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HRManagement.ServiceDefaults.Workflow;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class RecruitmentRequisitionPolicyIntegrationTests
{
    [Fact]
    public async Task PublishAsync_Succeeds_In_Off_Mode_For_Approved_Requisition()
    {
        await using var db = CreateContext();
        await LegislationRegisterSeeder.SeedAsync(db);
        var org = new OrgUnit { Code = "OU-PUB", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-PUB",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-PUB-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Approved,
            RequestedDate = DateTime.UtcNow,
            ApprovedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = "Off" })
            .Build();
        var lifecycle = new RecruitmentRequisitionLifecycleService(
            db,
            new PolicyOrchestrator(db, Mock.Of<IWorkflowStarter>(), new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())), NullLogger<PolicyOrchestrator>.Instance),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            Mock.Of<IWorkflowStarter>(),
            config);

        var (success, error) = await lifecycle.PublishAsync(req, Guid.NewGuid(), "test");
        Assert.True(success, error);
        Assert.Equal(RequisitionStatus.Published, (await db.RecruitmentRequisitions.SingleAsync()).Status);
        Assert.True(await db.PublicJobPostings.AnyAsync(p => p.RequisitionId == req.Id));
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
