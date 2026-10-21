using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.SharedKernel.Legal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using HR.Core.Infrastructure.Services.Policy;
using Moq;

namespace HRManagement.UnitTests;

public class Syr001RecruitmentLegalSourceTests
{
    private static async Task<(HRCoreDbContext Db, RecruitmentLegalSourceValidator Validator, RecruitmentRequisitionLifecycleService Lifecycle)>
        CreateSeededLifecycleAsync(string policyMode = "Off")
    {
        var options = new DbContextOptionsBuilder<HRCoreDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new HRCoreDbContext(options);
        await LegislationRegisterSeeder.SeedAsync(db);

        var provider = new LegislationRegisterProvider(db);
        var validator = new RecruitmentLegalSourceValidator(provider);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PolicyEngine:DefaultMode"] = policyMode })
            .Build();
        var lifecycle = new RecruitmentRequisitionLifecycleService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            validator,
            Mock.Of<IWorkflowStarter>(),
            config);

        return (db, validator, lifecycle);
    }

    [Fact]
    public async Task ValidateOptional_allows_null()
    {
        var (_, validator, _) = await CreateSeededLifecycleAsync();
        Assert.Null(await validator.ValidateOptionalAsync(null));
        Assert.Null(await validator.ValidateOptionalAsync("   "));
    }

    [Fact]
    public async Task ValidateOptional_rejects_unknown_id()
    {
        var (_, validator, _) = await CreateSeededLifecycleAsync();
        var error = await validator.ValidateOptionalAsync("SYR-LAW-17-2010");
        Assert.Equal(RecruitmentLegalSourceRules.NotFoundMessage, error);
    }

    [Fact]
    public async Task ValidateRequired_rejects_empty()
    {
        var (_, validator, _) = await CreateSeededLifecycleAsync();
        var error = await validator.ValidateRequiredAsync(null);
        Assert.Equal(RecruitmentLegalSourceRules.RequiredOnSubmitMessage, error);
    }

    [Fact]
    public async Task Submit_fails_without_legal_source()
    {
        var (db, _, lifecycle) = await CreateSeededLifecycleAsync();
        var (req, _) = await SeedDraftRequisitionAsync(db);

        var (success, error) = await lifecycle.SubmitForApprovalAsync(req, Guid.NewGuid(), "test");

        Assert.False(success);
        Assert.Equal(RecruitmentLegalSourceRules.RequiredOnSubmitMessage, error);
    }

    [Fact]
    public async Task Submit_succeeds_with_law50()
    {
        var (db, _, lifecycle) = await CreateSeededLifecycleAsync();
        var (req, _) = await SeedDraftRequisitionAsync(db, Syr001LegislationIds.Law50_2004);

        var (success, error) = await lifecycle.SubmitForApprovalAsync(req, Guid.NewGuid(), "test");

        Assert.True(success, error);
        Assert.Equal(RequisitionStatus.PendingApproval, (await db.RecruitmentRequisitions.SingleAsync()).Status);
    }

    private static async Task<(RecruitmentRequisition Req, FundedPosition Position)> SeedDraftRequisitionAsync(
        HRCoreDbContext db,
        string? legalSourceId = null)
    {
        var org = new OrgUnit { Code = "OU-LS", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-LS",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-LS-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = legalSourceId
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();
        return (req, position);
    }
}
