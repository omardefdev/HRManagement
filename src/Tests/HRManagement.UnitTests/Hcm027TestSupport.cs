using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Integration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>HCM-027: shared SQLite fixtures for the offer merit/outcome tests.</summary>
internal static class Hcm027TestSupport
{
    public static readonly Guid CreatorId = Guid.Parse("27270000-0000-4000-8000-000000000001");
    public static readonly Guid ApproverA = Guid.Parse("27270000-0000-4000-8000-00000000000a");
    public static readonly Guid ApproverB = Guid.Parse("27270000-0000-4000-8000-00000000000b");
    public static readonly Guid ApproverC = Guid.Parse("27270000-0000-4000-8000-00000000000c");

    private static int _offerCounter;

    public const string ValidOverrideReason = "المرشح الأول اعتذر شفهياً واللجنة أوصت بالانتقال للمرشح التالي";

    public static HRCoreDbContext CreateContext()
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

    public static IConfiguration Config(string policyMode = "Off", bool labScale = true) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PolicyEngine:DefaultMode"] = "Off",
                ["PolicyEngine:Core.Recruitment"] = policyMode,
                ["NationalScope:IsLabScale"] = labScale ? "true" : "false"
            })
            .Build();

    public static RecruitmentOfferService CreateOfferService(
        HRCoreDbContext db,
        IConfiguration? config = null,
        IPolicyOrchestrator? orchestrator = null,
        RecruitmentOptions? options = null,
        IPayrollGradeLookup? gradeLookup = null)
    {
        var codeGenerator = new Mock<ICodeGenerator>();
        codeGenerator
            .Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CodeGenerationResult { Code = $"OFF-HCM27-{Interlocked.Increment(ref _offerCounter):00000}" });

        return new RecruitmentOfferService(
            db,
            orchestrator ?? Mock.Of<IPolicyOrchestrator>(),
            codeGenerator.Object,
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            CreateStarter(db),
            config ?? Config(),
            Microsoft.Extensions.Options.Options.Create(options ?? new RecruitmentOptions()),
            gradeLookup ?? new FakePayrollGradeLookup());
    }

    public static WorkflowStarter CreateStarter(HRCoreDbContext db) =>
        new(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);

    public static PolicyOrchestrator CreateOrchestrator(HRCoreDbContext db) =>
        new(
            db,
            CreateStarter(db),
            new PolicyPublishedSnapshotCache(new MemoryCache(new MemoryCacheOptions())),
            NullLogger<PolicyOrchestrator>.Instance);

    public static WorkflowInstanceActionService CreateActionService(HRCoreDbContext db) =>
        new(
            db,
            new WorkflowEngine(),
            Mock.Of<IWorkflowSignatureService>(),
            Mock.Of<MassTransit.IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, null!));

    /// <summary>Legislation register + national workflow pack (incl. both offer workflows).</summary>
    public static async Task SeedReferenceDataAsync(HRCoreDbContext db)
    {
        await LegislationRegisterSeeder.SeedAsync(db);
        await WorkflowPackSeeder.SeedAsync(db);
    }

    public static async Task<RecruitmentRequisition> SeedRequisitionAsync(HRCoreDbContext db, string suffix = "A")
    {
        if (!await db.LegislationSources.AnyAsync())
            await LegislationRegisterSeeder.SeedAsync(db);

        var org = new OrgUnit { Code = $"OU-H27-{suffix}", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = $"VAC-H27-{suffix}",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = $"REQ-H27-{suffix}",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Published,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();
        return req;
    }

    public static async Task<RecruitmentApplication> SeedApplicationAsync(
        HRCoreDbContext db,
        RecruitmentRequisition req,
        RecruitmentApplicationStatus status,
        int? rank,
        string name = "مرشح",
        RecruitmentAppealStatus appeal = RecruitmentAppealStatus.None)
    {
        var app = new RecruitmentApplication
        {
            ApplicantFullName = name,
            RequisitionId = req.Id,
            AppliedPosition = req.JobTitle,
            AppliedAt = DateTime.UtcNow,
            Status = status,
            Rank = rank,
            AppealStatus = appeal
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        return app;
    }

    public static async Task<string?> GetWorkflowCodeAsync(HRCoreDbContext db, Guid? instanceId)
    {
        if (instanceId is not Guid id)
            return null;
        var instance = await db.WorkflowInstances.AsNoTracking().SingleAsync(i => i.Id == id);
        return await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Id == instance.DefinitionId)
            .Select(d => d.Code)
            .SingleAsync();
    }
}
