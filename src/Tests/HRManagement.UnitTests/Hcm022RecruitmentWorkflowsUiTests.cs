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

public class Hcm022RecruitmentWorkflowsUiTests
{
    private static readonly Guid CreatorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-111111111111");
    private static readonly Guid HrReviewerId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-222222222222");
    private static readonly Guid ComplianceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-333333333333");

    [Fact]
    public void Required_plan22_artifacts_exist()
    {
        var files = new[]
        {
            "docs/acceptance-plans/PLAN-22-hcm-recruitment-workflows-ui.md",
            "docs/acceptance-plans/execution/PLAN-22-EXECUTION.md",
            "scripts/hcm022-validate.ps1",
            "src/Services/HRCore/HR.Core.Web/Services/WorkflowPanelBuilder.cs",
            "src/Services/HRCore/HR.Core.Web/Services/OfferLetterWorkflowUiService.cs",
            "src/Services/HRCore/HR.Core.Web/Services/SelectionCommitteeWorkflowUiService.cs",
            "src/Services/HRCore/HR.Core.Web/Services/RecruitmentAppealWorkflowUiService.cs",
            "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/SelectionCommitteeWorkflowHandler.cs",
            "src/Services/HRCore/HR.Core.Infrastructure/Services/Recruitment/RecruitmentAppealWorkflowHandler.cs",
            "src/Tests/HRManagement.UnitTests/Hcm022RecruitmentWorkflowsUiTests.cs",
            "src/Tests/HRManagement.AcceptanceTests/AcHcm22RecruitmentWorkflowsUiTests.cs"
        };

        foreach (var rel in files)
            Assert.True(System.IO.File.Exists(RepoPaths.Resolve(rel)), $"missing {rel}");
    }

    [Fact]
    public async Task OfferUiService_blocks_creator_from_approving()
    {
        await using var db = CreateContext();
        var offer = await SeedPendingOfferAsync(db, CreatorId);
        offer.WorkflowInstanceId = (await db.WorkflowInstances.SingleAsync()).Id;
        await db.SaveChangesAsync();

        var service = new OfferLetterWorkflowUiService(db, WorkflowInboxTestSupport.CreateEstablishmentScopeService(db));
        var vm = await service.BuildDetailsAsync(offer, CreateUser(CreatorId, "hr-core"));

        Assert.False(vm.Workflow.CanApprove);
        Assert.Equal("لا يمكن للمنشئ اعتماد عرضه الخاص.", vm.Workflow.BlockReason);
    }

    [Fact]
    public async Task SubmitOffer_lab_scale_starts_workflow()
    {
        await using var db = CreateContext();
        await LegislationRegisterSeeder.SeedAsync(db);
        var offer = await SeedDraftOfferAsync(db);

        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Code = "RECRUITMENT-OFFER-APPROVAL",
            Name = "اعتماد عرض",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "OfferLetter",
            StepsJson = """[{"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"}]""",
            GuardrailRolesJson = "[]"
        });
        await db.SaveChangesAsync();

        var config = LabScaleConfig();
        var starter = new WorkflowStarter(db, new WorkflowEngine(), NullLogger<WorkflowStarter>.Instance);
        var offerService = new RecruitmentOfferService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            Mock.Of<HRManagement.SharedKernel.CodeGeneration.ICodeGenerator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            starter,
            config,
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions()));

        var (success, error) = await offerService.SubmitForApprovalAsync(offer.Id, CreatorId, "creator");
        Assert.True(success, error);

        db.ChangeTracker.Clear();
        var saved = await db.OfferLetters.SingleAsync();
        Assert.Equal(OfferStatus.PendingApproval, saved.Status);
        Assert.NotNull(saved.WorkflowInstanceId);
    }

    [Fact]
    public async Task CommitteeUiService_blocks_quorum_on_step2()
    {
        await using var db = CreateContext();
        var (session, instance) = await SeedCommitteeSessionAsync(db, memberCount: 2);
        session.WorkflowInstanceId = instance.Id;
        await db.SaveChangesAsync();

        instance.CurrentStepLevel = 2;
        await db.SaveChangesAsync();

        var service = new SelectionCommitteeWorkflowUiService(db, WorkflowInboxTestSupport.CreateEstablishmentScopeService(db));
        var vm = await service.BuildDetailsAsync(session, CreateUser(HrReviewerId, "hr-core"));

        Assert.False(vm.Workflow.CanApprove);
        Assert.Contains("نصاب", vm.Workflow.BlockReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppealUiService_blocks_non_reviewer()
    {
        await using var db = CreateContext();
        var (appeal, instance) = await SeedPendingAppealAsync(db, ComplianceId);
        appeal.WorkflowInstanceId = instance.Id;
        await db.SaveChangesAsync();

        var service = new RecruitmentAppealWorkflowUiService(db);
        var panel = await service.BuildPanelAsync(appeal, CreateUser(HrReviewerId, "compliance"));
        Assert.NotNull(panel);
        Assert.False(panel!.CanApprove);
        Assert.Contains("مراجع", panel.BlockReason ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommitteeSubmit_rejects_without_members()
    {
        await using var db = CreateContext();
        var org = new OrgUnit { Code = "OU-COMM-2", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-COMM-2",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-COMM-002",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Approved,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(requisition);
        await db.SaveChangesAsync();

        var session = new SelectionCommitteeSession
        {
            RequisitionId = requisition.Id,
            CommitteeDate = DateTime.UtcNow
        };
        db.SelectionCommitteeSessions.Add(session);
        await db.SaveChangesAsync();

        var lifecycle = new SelectionCommitteeLifecycleService(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            Mock.Of<IWorkflowStarter>(),
            LabScaleConfig());

        var (success, error) = await lifecycle.SubmitForApprovalAsync(session.Id, CreatorId, "creator");
        Assert.False(success);
        Assert.Contains("عضو", error ?? string.Empty, StringComparison.Ordinal);
    }

    private static async Task<(Guid ApplicationId, Guid FundedPositionId)> SeedOfferGraphAsync(HRCoreDbContext db)
    {
        var org = new OrgUnit { Code = "OU-HCM22", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-HCM22",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var app = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح",
            Status = RecruitmentApplicationStatus.OfferExtended,
            AppliedAt = DateTime.UtcNow
        };
        db.RecruitmentApplications.Add(app);
        await db.SaveChangesAsync();
        return (app.Id, position.Id);
    }

    private static async Task<OfferLetter> SeedPendingOfferAsync(HRCoreDbContext db, Guid creatorId)
    {
        var (applicationId, fundedPositionId) = await SeedOfferGraphAsync(db);
        var definition = new WorkflowDefinition
        {
            Code = "RECRUITMENT-OFFER-APPROVAL",
            Name = "اعتماد",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "OfferLetter",
            StepsJson = """[{"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"}]""",
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var offer = new OfferLetter
        {
            OfferNumber = "OFF-UI-001",
            Status = OfferStatus.PendingApproval,
            RecruitmentApplicationId = applicationId,
            FundedPositionId = fundedPositionId,
            OfferedSalary = 1000,
            ProposedStartDate = DateTime.UtcNow.Date,
            CreatedBy = creatorId
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "OfferLetter",
            EntityId = offer.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = creatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return offer;
    }

    private static async Task<OfferLetter> SeedDraftOfferAsync(HRCoreDbContext db)
    {
        var (applicationId, fundedPositionId) = await SeedOfferGraphAsync(db);
        var offer = new OfferLetter
        {
            OfferNumber = "OFF-HCM22-001",
            Status = OfferStatus.Draft,
            RecruitmentApplicationId = applicationId,
            FundedPositionId = fundedPositionId,
            OfferedSalary = 1000,
            ProposedStartDate = DateTime.UtcNow.Date,
            LegalSourceId = Syr001LegislationIds.Law50_2004
        };
        db.OfferLetters.Add(offer);
        await db.SaveChangesAsync();
        return offer;
    }

    private static async Task<(SelectionCommitteeSession Session, WorkflowInstance Instance)> SeedCommitteeSessionAsync(
        HRCoreDbContext db,
        int memberCount)
    {
        var definition = new WorkflowDefinition
        {
            Code = "RECRUITMENT-SELECTION-COMMITTEE",
            Name = "لجنة",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "SelectionCommitteeSession",
            StepsJson = """
                [
                  {"Level":1,"Name":"تشكيل","StepType":0,"RoleRequired":"hr-core"},
                  {"Level":2,"Name":"نصاب","StepType":0,"RoleRequired":"hr-core"}
                ]
                """,
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var org = new OrgUnit { Code = "OU-COMM-3", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-COMM-3",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-COMM-003",
            FundedPositionId = position.Id,
            JobTitle = "محلل",
            Status = RequisitionStatus.Approved,
            RequestedDate = DateTime.UtcNow,
            VacancyCount = 1
        };
        db.RecruitmentRequisitions.Add(requisition);
        await db.SaveChangesAsync();

        var session = new SelectionCommitteeSession
        {
            RequisitionId = requisition.Id,
            CommitteeDate = DateTime.UtcNow
        };
        for (var i = 0; i < memberCount; i++)
        {
            var employee = new Employee
            {
                FullName = $"عضو {i + 1}",
                NationalId = $"COMM{i + 1:0000000000}",
                PrimaryOrgUnitId = org.Id
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            session.Members.Add(new SelectionCommitteeMember
            {
                EmployeeId = employee.Id,
                Role = "عضو"
            });
        }
        db.SelectionCommitteeSessions.Add(session);
        await db.SaveChangesAsync();

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "SelectionCommitteeSession",
            EntityId = session.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = CreatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (session, instance);
    }

    private static async Task<(RecruitmentAppeal Appeal, WorkflowInstance Instance)> SeedPendingAppealAsync(
        HRCoreDbContext db,
        Guid reviewerId)
    {
        var definition = new WorkflowDefinition
        {
            Code = "RECRUITMENT-APPEAL-REVIEW",
            Name = "استئناف",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "RecruitmentAppeal",
            StepsJson = """[{"Level":1,"Name":"مراجع","StepType":0,"RoleRequired":"compliance"}]""",
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var app = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح",
            Status = RecruitmentApplicationStatus.Rejected,
            AppliedAt = DateTime.UtcNow
        };
        db.RecruitmentApplications.Add(app);

        var appeal = new RecruitmentAppeal
        {
            RecruitmentApplicationId = app.Id,
            Reason = "سبب",
            Status = RecruitmentAppealStatus.Pending,
            IndependentReviewerId = reviewerId
        };
        db.RecruitmentAppeals.Add(appeal);
        await db.SaveChangesAsync();

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = "RecruitmentAppeal",
            EntityId = appeal.Id,
            CurrentStepLevel = 1,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = CreatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (appeal, instance);
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
