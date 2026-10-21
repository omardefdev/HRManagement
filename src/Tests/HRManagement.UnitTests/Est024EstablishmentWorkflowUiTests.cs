using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.MasterData;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Workflow;
using HRManagement.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>EST-024 / PLAN-24: establishment change workflow UI + inbox.</summary>
public class Est024EstablishmentWorkflowUiTests
{
    private static readonly Guid CreatorId = Guid.Parse("dddddddd-dddd-dddd-dddd-111111111111");
    private static readonly Guid HrReviewerId = Guid.Parse("dddddddd-dddd-dddd-dddd-222222222222");
    private static readonly Guid MgmtApproverId = Guid.Parse("dddddddd-dddd-dddd-dddd-333333333333");

    [Fact]
    public void Required_plan24_artifacts_exist()
    {
        var files = new[]
        {
            "docs/acceptance-plans/PLAN-24-establishment-workflow-ui.md",
            "docs/acceptance-plans/execution/PLAN-24-EXECUTION.md",
            "scripts/est024-validate.ps1",
            "src/Services/HRCore/HR.Core.Web/Services/EstablishmentChangeWorkflowUiService.cs",
            "src/Services/HRCore/HR.Core.Web/Views/EstablishmentChanges/_EstablishmentChangeWorkflowPanel.cshtml",
            "src/Tests/HRManagement.UnitTests/Est024EstablishmentWorkflowUiTests.cs",
            "src/Tests/HRManagement.AcceptanceTests/AcEst24EstablishmentWorkflowUiTests.cs"
        };

        foreach (var rel in files)
            Assert.True(System.IO.File.Exists(RepoPaths.Resolve(rel)), $"missing {rel}");
    }

    [Fact]
    public async Task Submit_lab_scale_starts_workflow_without_auto_apply()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);

        db.WorkflowDefinitions.Add(CreateDefinition());
        await db.SaveChangesAsync();

        var lifecycle = CreateLifecycle(db, LabScaleConfig());
        var request = await lifecycle.CreateDraftAsync(
            EstablishmentChangeType.CreateJobCatalog,
            new { JobCode = "", TitleAr = "محلل", GradeCode = "G5", OccupationalGroup = "JC-ADM" },
            createdBy: CreatorId);

        var (success, error) = await lifecycle.SubmitForApprovalAsync(
            request.Id,
            Syr001LegislationIds.Law50_2004,
            decisionReference: "DEC-EST24",
            actorUserId: CreatorId,
            actorUserName: "central");

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
        Assert.True(await db.WorkflowInstances.AnyAsync(i => i.Id == saved.WorkflowInstanceId));
    }

    [Fact]
    public async Task UiService_blocks_creator_from_approving()
    {
        await using var db = CreateContext();
        var (request, _) = await SeedPendingChangeAsync(db, CreatorId, stepLevel: 1);

        var service = new EstablishmentChangeWorkflowUiService(db, WorkflowInboxTestSupport.CreateEstablishmentScopeService(db));
        var vm = await service.BuildDetailsAsync(request, CreateUser(CreatorId, "hr-core"));

        Assert.False(vm.Workflow.CanApprove);
        Assert.Equal("لا يمكن للمنشئ اعتماد طلبه الخاص.", vm.Workflow.BlockReason);
    }

    [Fact]
    public async Task Inbox_shows_step1_for_hr_core_and_step2_for_mgmt_approver()
    {
        await using var db = CreateContext();
        await SeedPendingChangeAsync(db, CreatorId, stepLevel: 1);
        await SeedPendingChangeAsync(db, CreatorId, stepLevel: 2, requestNumberSuffix: "-2");

        var query = CreateQueryService(db);
        var hrItems = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));
        var mgmtItems = await query.GetActionableWorkflowItemsAsync(CreateUser(MgmtApproverId, "hr-management-approver"));

        Assert.Single(hrItems);
        Assert.Contains("الخطوة 1", hrItems[0].Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Single(mgmtItems);
        Assert.Contains("الخطوة 2", mgmtItems[0].Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Creator_excluded_from_inbox()
    {
        await using var db = CreateContext();
        await SeedPendingChangeAsync(db, CreatorId, stepLevel: 1);

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(CreatorId, "hr-core"));
        Assert.Empty(items);
    }

    [Fact]
    public async Task Deep_link_resolves_to_establishment_details()
    {
        await using var db = CreateContext();
        var (request, _) = await SeedPendingChangeAsync(db, CreatorId, stepLevel: 1);

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));

        Assert.Single(items);
        Assert.Equal($"/EstablishmentChanges/Details/{request.Id}", items[0].DeepLink);
    }

    [Fact]
    public async Task ActionService_two_step_approve_applies_change()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        var (request, instance) = await SeedPendingChangeAsync(db, CreatorId, stepLevel: 1);

        var actionService = CreateActionService(db);
        var step1 = await actionService.ApproveAsync(
            instance.Id,
            new WorkflowActionActor(HrReviewerId, "hr", "hr-core"),
            comments: "ok",
            targetStepLevel: 1);
        Assert.True(step1.Success, step1.Error);

        db.ChangeTracker.Clear();
        instance = await db.WorkflowInstances.SingleAsync(i => i.Id == instance.Id);
        Assert.Equal(2, instance.CurrentStepLevel);

        var step2 = await actionService.ApproveAsync(
            instance.Id,
            new WorkflowActionActor(MgmtApproverId, "mgmt", "hr-management-approver"),
            comments: "ok",
            targetStepLevel: 2);
        Assert.True(step2.Success, step2.Error);

        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(EstablishmentChangeStatus.Applied, saved.Status);
        Assert.True(await db.JobCatalogEntries.AnyAsync(e => e.TitleAr == "محلل"));
    }

    [Fact]
    public async Task Reject_returns_request_to_draft()
    {
        await using var db = CreateContext();
        var (request, instance) = await SeedPendingChangeAsync(db, CreatorId, stepLevel: 1);

        var actionService = CreateActionService(db);
        var result = await actionService.RejectAsync(
            instance.Id,
            new WorkflowActionActor(HrReviewerId, "hr", "hr-core"),
            comments: "no");

        Assert.True(result.Success, result.Error);
        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(EstablishmentChangeStatus.Draft, saved.Status);
        Assert.Null(saved.WorkflowInstanceId);
    }

    [Fact]
    public async Task Enforce_submit_sets_pending_without_auto_apply()
    {
        await using var db = CreateContext();
        await EstablishmentMdmTestHelper.SeedReferenceCatalogAsync(db);
        await LegislationRegisterSeeder.SeedAsync(db);
        await PolicyEngineDomainSeeds.SeedAllDomainsAsync(db);

        db.WorkflowDefinitions.Add(CreateDefinition());
        await db.SaveChangesAsync();

        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
        var orchestrator = new PolicyOrchestrator(
            db,
            starter,
            new PolicyPublishedSnapshotCache(new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())),
            NullLogger<PolicyOrchestrator>.Instance);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PolicyEngine:Core.Establishment"] = "Enforce",
                ["NationalScope:IsLabScale"] = "true"
            })
            .Build();

        var lifecycle = new EstablishmentChangeLifecycleService(
            db,
            CreateCodeGeneratorMock().Object,
            orchestrator,
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db))),
            CreateApplier(db),
            starter,
            config);

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-ENF-001",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.Draft,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"G5","OccupationalGroup":"JC-ADM"}""",
            RequestedAt = DateTime.UtcNow,
            CreatedBy = CreatorId
        };
        db.EstablishmentChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var (success, error) = await lifecycle.SubmitForApprovalAsync(
            request.Id,
            Syr001LegislationIds.Law50_2004,
            decisionReference: "DEC-ENF",
            actorUserId: CreatorId,
            actorUserName: "central");

        Assert.True(success, error);
        db.ChangeTracker.Clear();
        var saved = await db.EstablishmentChangeRequests.SingleAsync();
        Assert.Equal(EstablishmentChangeStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
    }

    private static async Task<(EstablishmentChangeRequest Request, WorkflowInstance Instance)> SeedPendingChangeAsync(
        HRCoreDbContext db,
        Guid creatorId,
        int stepLevel,
        string requestNumberSuffix = "")
    {
        WorkflowDefinition? definition = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Code == "ESTABLISHMENT-CHANGE-APPROVAL");
        if (definition is null)
        {
            definition = CreateDefinition();
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
        }

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = $"EST-24{requestNumberSuffix}",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = """{"JobCode":"","TitleAr":"محلل","GradeCode":"G5","OccupationalGroup":"JC-ADM"}""",
            RequestedAt = DateTime.UtcNow,
            CreatedBy = creatorId
        };
        db.EstablishmentChangeRequests.Add(request);
        await db.SaveChangesAsync();

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "EstablishmentChangeRequest",
            EntityId = request.Id,
            CurrentStepLevel = stepLevel,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = creatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        request.WorkflowInstanceId = instance.Id;
        await db.SaveChangesAsync();
        return (request, instance);
    }

    private static WorkflowDefinition CreateDefinition() => new()
    {
        Code = "ESTABLISHMENT-CHANGE-APPROVAL",
        Name = "اعتماد تغيير ملاك",
        Version = 1,
        IsPublished = true,
        DomainEntityType = "EstablishmentChangeRequest",
        StepsJson = """
            [
              {"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"},
              {"Level":2,"Name":"اعتماد إداري","StepType":0,"RoleRequired":"hr-management-approver"}
            ]
            """,
        GuardrailRolesJson = "[]"
    };

    private static EstablishmentChangeLifecycleService CreateLifecycle(HRCoreDbContext db, IConfiguration config)
    {
        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
        return new EstablishmentChangeLifecycleService(
            db,
            CreateCodeGeneratorMock().Object,
            Mock.Of<IPolicyOrchestrator>(),
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db))),
            CreateApplier(db),
            starter,
            config);
    }

    private static Mock<ICodeGenerator> CreateCodeGeneratorMock()
    {
        var mock = new Mock<ICodeGenerator>();
        mock.Setup(g => g.GenerateAsync(It.IsAny<CodeGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CodeGenerationResult { Code = "EST-DRAFT-001" });
        return mock;
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
            new HR.Core.Domain.Services.EstablishmentService(),
            fundedCode.Object,
            jobCatalogCode.Object,
            new EstablishmentReferenceValidator(new ReferenceDataProvider(db)),
            new JobCatalogVersionService(db),
            new FundedPositionVersionService(db));
    }

    private static WorkflowInstanceActionService CreateActionService(HRCoreDbContext db)
    {
        var lifecycle = new EstablishmentChangeLifecycleService(
            db,
            CreateCodeGeneratorMock().Object,
            Mock.Of<IPolicyOrchestrator>(),
            new EstablishmentLegalSourceValidator(new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db))),
            CreateApplier(db),
            Mock.Of<IWorkflowStarter>(),
            LabScaleConfig());

        return new WorkflowInstanceActionService(
            db,
            new WorkflowEngine(),
            Mock.Of<IWorkflowSignatureService>(),
            Mock.Of<MassTransit.IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, lifecycle));
    }

    private static WorkflowInboxQueryService CreateQueryService(HRCoreDbContext db)
    {
        var eligibility = new WorkflowInboxEligibilityService();
        var resolver = new WorkflowDeepLinkResolver(db);
        var orgScope = WorkflowInboxTestSupport.CreateOrgScopeService(db);
        return new WorkflowInboxQueryService(db, eligibility, resolver, orgScope);
    }

    private static IConfiguration LabScaleConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PolicyEngine:DefaultMode"] = "Off",
                ["NationalScope:IsLabScale"] = "true"
            })
            .Build();

    private static ClaimsPrincipal CreateUser(Guid userId, string role) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        ], "Test"));

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
