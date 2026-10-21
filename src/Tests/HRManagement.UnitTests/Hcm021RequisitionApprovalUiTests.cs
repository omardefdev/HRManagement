using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Workflow;
using HRManagement.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HRManagement.UnitTests;

public class Hcm021RequisitionApprovalUiTests
{
    private static readonly Guid CreatorId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-111111111111");
    private static readonly Guid HrReviewerId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-222222222222");

    [Fact]
    public void Required_plan21_artifacts_exist()
    {
        var files = new[]
        {
            "docs/acceptance-plans/PLAN-21-hcm-requisition-approval-ui.md",
            "docs/acceptance-plans/execution/PLAN-21-EXECUTION.md",
            "scripts/hcm021-validate.ps1",
            "src/Services/HRCore/HR.Core.Infrastructure/Services/Workflow/WorkflowInstanceActionService.cs",
            "src/Services/HRCore/HR.Core.Web/Services/RecruitmentRequisitionWorkflowUiService.cs",
            "src/Services/HRCore/HR.Core.Web/Views/RecruitmentRequisitions/_RequisitionWorkflowPanel.cshtml",
            "src/Services/HRCore/HR.Core.Web/Views/Shared/_WorkflowProgressPanel.cshtml",
            "src/Services/HRCore/HR.Core.Web/Views/Shared/_WorkflowActionPanel.cshtml",
            "src/Tests/HRManagement.UnitTests/Hcm021RequisitionApprovalUiTests.cs",
            "src/Tests/HRManagement.AcceptanceTests/AcHcm21RequisitionApprovalUiTests.cs"
        };

        foreach (var rel in files)
            Assert.True(System.IO.File.Exists(RepoPaths.Resolve(rel)), $"missing {rel}");
    }

    [Fact]
    public async Task UiService_blocks_creator_from_approving()
    {
        await using var db = CreateContext();
        var (requisition, instance) = await SeedPendingRequisitionAsync(db, CreatorId);
        requisition.WorkflowInstanceId = instance.Id;
        await db.SaveChangesAsync();

        var service = new RecruitmentRequisitionWorkflowUiService(db, WorkflowInboxTestSupport.CreateEstablishmentScopeService(db));
        var vm = await service.BuildDetailsAsync(requisition, CreateUser(CreatorId, "hr-core"));

        Assert.False(vm.Workflow.CanApprove);
        Assert.Equal("لا يمكن للمنشئ اعتماد طلبه الخاص.", vm.Workflow.BlockReason);
    }

    [Fact]
    public async Task UiService_allows_hr_reviewer_on_step1()
    {
        await using var db = CreateContext();
        var (requisition, instance) = await SeedPendingRequisitionAsync(db, CreatorId);
        requisition.WorkflowInstanceId = instance.Id;
        await db.SaveChangesAsync();

        var service = new RecruitmentRequisitionWorkflowUiService(db, WorkflowInboxTestSupport.CreateEstablishmentScopeService(db));
        var vm = await service.BuildDetailsAsync(requisition, CreateUser(HrReviewerId, "hr-core"));

        Assert.True(vm.Workflow.CanApprove);
        Assert.True(vm.Workflow.CanReject);
        Assert.Equal(2, vm.Workflow.Steps.Count);
        Assert.Equal("current", vm.Workflow.Steps[0].State);
    }

    [Fact]
    public async Task SubmitForApproval_lab_scale_starts_workflow()
    {
        await using var db = CreateContext();
        await LegislationRegisterSeeder.SeedAsync(db);
        var org = new OrgUnit { Code = "OU-HCM21", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-HCM21",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-HCM21-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Draft,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.RecruitmentRequisitions.Add(req);

        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Code = "RECRUITMENT-REQUISITION-APPROVAL",
            Name = "اعتماد طلب شاغر",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "RecruitmentRequisition",
            StepsJson = """[{"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"}]""",
            GuardrailRolesJson = "[]"
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PolicyEngine:DefaultMode"] = "Off",
                ["NationalScope:IsLabScale"] = "true"
            })
            .Build();

        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
        var lifecycle = new RecruitmentRequisitionLifecycleService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            starter,
            config);

        var (success, error) = await lifecycle.SubmitForApprovalAsync(req, CreatorId, "creator");
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        var saved = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(RequisitionStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
        Assert.True(await db.WorkflowInstances.AnyAsync(i => i.Id == saved.WorkflowInstanceId));
    }

    [Fact]
    public async Task ActionService_approve_completes_requisition()
    {
        await using var db = CreateContext();
        var (requisition, instance) = await SeedPendingRequisitionAsync(db, CreatorId, singleStep: true);
        requisition.WorkflowInstanceId = instance.Id;
        requisition.Status = RequisitionStatus.PendingApproval;
        await db.SaveChangesAsync();

        var service = new WorkflowInstanceActionService(
            db,
            new WorkflowEngine(),
            Mock.Of<IWorkflowSignatureService>(),
            Mock.Of<MassTransit.IPublishEndpoint>(),
            new RecruitmentRequisitionWorkflowHandler(db),
            new OfferLetterWorkflowHandler(db),
            new SelectionCommitteeWorkflowHandler(db),
            new RecruitmentAppealWorkflowHandler(db),
            new EstablishmentChangeWorkflowHandler(db, null!));

        var result = await service.ApproveAsync(
            instance.Id,
            new WorkflowActionActor(HrReviewerId, "reviewer", "hr-core"),
            comments: "ok",
            targetStepLevel: 1);

        Assert.True(result.Success, result.Error);

        db.ChangeTracker.Clear();
        var savedReq = await db.RecruitmentRequisitions.SingleAsync();
        Assert.Equal(RequisitionStatus.Approved, savedReq.Status);
        Assert.NotNull(savedReq.ApprovedDate);
    }

    private static async Task<(RecruitmentRequisition Req, WorkflowInstance Instance)> SeedPendingRequisitionAsync(
        HRCoreDbContext db,
        Guid creatorId,
        bool singleStep = false)
    {
        var stepsJson = singleStep
            ? """[{"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"}]"""
            : """
                [
                  {"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"},
                  {"Level":2,"Name":"اعتماد إداري","StepType":0,"RoleRequired":"hr-management-approver"}
                ]
                """;
        var org = new OrgUnit { Code = "OU-UI", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-UI",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);

        var definition = new WorkflowDefinition
        {
            Code = "RECRUITMENT-REQUISITION-APPROVAL",
            Name = "اعتماد",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "RecruitmentRequisition",
            StepsJson = stepsJson,
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var req = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-UI-001",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.PendingApproval,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1,
            CreatedBy = creatorId
        };
        db.RecruitmentRequisitions.Add(req);
        await db.SaveChangesAsync();

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "RecruitmentRequisition",
            EntityId = req.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = creatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (req, instance);
    }

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
