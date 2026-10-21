using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Workflow;
using HRManagement.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

/// <summary>WF-023 / PLAN-23: unified work inbox query + service.</summary>
public class Wf023WorkInboxUiTests
{
    private static readonly Guid CreatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-111111111111");
    private static readonly Guid HrReviewerId = Guid.Parse("cccccccc-cccc-cccc-cccc-222222222222");
    private static readonly Guid MgmtApproverId = Guid.Parse("cccccccc-cccc-cccc-cccc-333333333333");
    private static readonly Guid ComplianceId = Guid.Parse("cccccccc-cccc-cccc-cccc-444444444444");

    [Fact]
    public void Required_plan23_artifacts_exist()
    {
        var files = new[]
        {
            "docs/acceptance-plans/PLAN-23-wf-work-inbox-ui.md",
            "docs/acceptance-plans/execution/PLAN-23-EXECUTION.md",
            "scripts/wf023-validate.ps1",
            "src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxQueryService.cs",
            "src/Services/HRCore/HR.Core.Web/Services/WorkflowInboxEligibilityService.cs",
            "src/Services/HRCore/HR.Core.Web/Services/WorkflowDeepLinkResolver.cs",
            "src/Services/HRCore/HR.Core.Web/Services/WorkInboxItemMapper.cs",
            "src/Services/HRCore/HR.Core.Web/Services/HrCoreWorkInboxService.cs",
            "src/Services/HRCore/HR.Core.Web/Controllers/WorkInboxController.cs",
            "src/Services/HRCore/HR.Core.Web/Views/WorkInbox/Index.cshtml",
            "src/Tests/HRManagement.UnitTests/Wf023WorkInboxUiTests.cs",
            "src/Tests/HRManagement.AcceptanceTests/AcWf23WorkInboxUiTests.cs"
        };

        foreach (var rel in files)
            Assert.True(System.IO.File.Exists(RepoPaths.Resolve(rel)), $"missing {rel}");
    }

    [Fact]
    public async Task HrCore_user_sees_requisition_and_offer_step1_items()
    {
        await using var db = CreateContext();
        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1);
        await SeedTwoStepOfferAsync(db, CreatorId, stepLevel: 1);

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));

        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.DeepLink!.Contains("RecruitmentRequisitions/Details", StringComparison.Ordinal));
        Assert.Contains(items, i => i.DeepLink!.Contains("OfferLetters/Details", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Management_approver_sees_step2_only_not_step1()
    {
        await using var db = CreateContext();
        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 2);
        await SeedTwoStepOfferAsync(db, CreatorId, stepLevel: 2);

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(MgmtApproverId, "hr-management-approver"));

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Contains("الخطوة 2", i.Description ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Management_approver_does_not_see_hr_core_step1()
    {
        await using var db = CreateContext();
        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1);

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(MgmtApproverId, "hr-management-approver"));

        Assert.Empty(items);
    }

    [Fact]
    public async Task Creator_excluded_from_own_requisition_and_offer()
    {
        await using var db = CreateContext();
        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1);
        await SeedTwoStepOfferAsync(db, CreatorId, stepLevel: 1);
        db.ChangeTracker.Clear();

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(CreatorId, "hr-core"));

        Assert.Empty(items);
    }

    [Fact]
    public async Task Compliance_reviewer_sees_appeal_only_when_assigned()
    {
        await using var db = CreateContext();
        var (appeal, instance) = await SeedAppealAsync(db, ComplianceId);
        appeal.WorkflowInstanceId = instance.Id;
        await db.SaveChangesAsync();

        var query = CreateQueryService(db);
        var reviewerItems = await query.GetActionableWorkflowItemsAsync(CreateUser(ComplianceId, "compliance"));
        var otherItems = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "compliance"));

        Assert.Single(reviewerItems);
        Assert.Contains("RecruitmentApplications/Details", reviewerItems[0].DeepLink!, StringComparison.Ordinal);
        Assert.Empty(otherItems);
    }

    [Fact]
    public async Task Pending_count_matches_actionable_inbox_length()
    {
        await using var db = CreateContext();
        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1);
        await SeedLegacyApprovalAsync(db, CreatorId, role: "hr-core");

        var user = CreateUser(HrReviewerId, "hr-core");
        var query = CreateQueryService(db);
        var inbox = CreateInboxService(db, user);

        var items = await query.GetActionableItemsAsync(user);
        var count = await inbox.GetPendingCountAsync(HrReviewerId);

        Assert.Equal(items.Count, count);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Deep_links_resolve_for_each_entity_type()
    {
        await using var db = CreateContext();
        var (reqId, _) = await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1);
        var (offerId, _) = await SeedTwoStepOfferAsync(db, CreatorId, stepLevel: 1);
        var (committee, committeeInstance) = await SeedCommitteeAsync(db, memberCount: 3);
        committee.WorkflowInstanceId = committeeInstance.Id;
        var (appeal, appealInstance) = await SeedAppealAsync(db, ComplianceId);
        appeal.WorkflowInstanceId = appealInstance.Id;
        await db.SaveChangesAsync();

        var query = CreateQueryService(db);
        var hrItems = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));
        var complianceItems = await query.GetActionableWorkflowItemsAsync(CreateUser(ComplianceId, "compliance"));

        Assert.Contains(hrItems, i => i.DeepLink == $"/RecruitmentRequisitions/Details/{reqId}");
        Assert.Contains(hrItems, i => i.DeepLink == $"/OfferLetters/Details/{offerId}");
        Assert.Contains(hrItems, i => i.DeepLink == $"/SelectionCommittees/Details/{committee.Id}");
        Assert.Contains(complianceItems, i => i.DeepLink!.StartsWith("/RecruitmentApplications/Details/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Establishment_change_appears_in_inbox_with_deep_link()
    {
        await using var db = CreateContext();
        var changeId = await SeedEstablishmentChangeAsync(db, CreatorId, stepLevel: 1);

        var query = CreateQueryService(db);
        var items = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));

        Assert.Contains(items, i => i.DeepLink == $"/EstablishmentChanges/Details/{changeId}");
    }

    [Fact]
    public async Task Legacy_approval_appears_for_matching_role()
    {
        await using var db = CreateContext();
        var legacyId = await SeedLegacyApprovalAsync(db, CreatorId, role: "hr-core");

        var query = CreateQueryService(db);
        var items = await query.GetActionableLegacyItemsAsync(CreateUser(HrReviewerId, "hr-core"));

        Assert.Single(items);
        Assert.Equal(legacyId, items[0].Id);
        Assert.Equal(WorkInboxItemMapper.SourceLegacy, items[0].SourceSystem);
        Assert.Equal($"/Approvals/Review/{legacyId}", items[0].DeepLink);
    }

    [Fact]
    public async Task Branch_manager_sees_only_in_scope_requisitions()
    {
        await using var db = CreateContext();
        var center = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        db.OrgUnits.AddRange(
            new OrgUnit { Id = center, Code = "HQ", NameAr = "مركز", NameEn = "HQ" },
            new OrgUnit { Id = branchA, Code = "BR-A", NameAr = "فرع أ", NameEn = "Branch A", ParentId = center },
            new OrgUnit { Id = branchB, Code = "BR-B", NameAr = "فرع ب", NameEn = "Branch B", ParentId = center });
        await db.SaveChangesAsync();
        await new OrgUnitPathService(db).RecomputeAllAsync();

        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1, orgUnitId: branchA, numberSuffix: "A");
        await SeedTwoStepRequisitionAsync(db, CreatorId, stepLevel: 1, orgUnitId: branchB, numberSuffix: "B");

        var query = CreateQueryService(db);
        var branchItems = await query.GetActionableWorkflowItemsAsync(
            CreateBranchManager(HrReviewerId, branchA, "hr-core"));
        var centralItems = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));

        Assert.Single(branchItems);
        Assert.Contains("REQ-WF23-A", branchItems[0].Title, StringComparison.Ordinal);
        Assert.Equal(2, centralItems.Count);
    }

    [Fact]
    public async Task Branch_manager_does_not_see_establishment_change_inbox_items()
    {
        await using var db = CreateContext();
        var branchRoot = Guid.NewGuid();
        db.OrgUnits.Add(new OrgUnit { Id = branchRoot, Code = "BR-EST", NameAr = "فرع", NameEn = "Branch" });
        await db.SaveChangesAsync();
        await new OrgUnitPathService(db).RecomputeAllAsync();

        await SeedEstablishmentChangeAsync(db, CreatorId, stepLevel: 1);

        var query = CreateQueryService(db);
        var branchItems = await query.GetActionableWorkflowItemsAsync(
            CreateBranchManager(HrReviewerId, branchRoot, "hr-core"));
        var centralItems = await query.GetActionableWorkflowItemsAsync(CreateUser(HrReviewerId, "hr-core"));

        Assert.Empty(branchItems);
        Assert.Single(centralItems);
    }

    [Fact]
    public async Task GetTeamInbox_returns_empty_stub()
    {
        await using var db = CreateContext();
        var inbox = CreateInboxService(db, CreateUser(HrReviewerId, "hr-core"));
        var team = await inbox.GetTeamInboxAsync("team-1");
        Assert.Empty(team);
    }

    private static WorkflowInboxQueryService CreateQueryService(HRCoreDbContext db)
    {
        var eligibility = new WorkflowInboxEligibilityService();
        var resolver = new WorkflowDeepLinkResolver(db);
        var orgScope = WorkflowInboxTestSupport.CreateOrgScopeService(db);
        return new WorkflowInboxQueryService(db, eligibility, resolver, orgScope);
    }

    private static HrCoreWorkInboxService CreateInboxService(HRCoreDbContext db, ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);
        return new HrCoreWorkInboxService(CreateQueryService(db), accessor.Object);
    }

    private static async Task<(Guid RequisitionId, Guid InstanceId)> SeedTwoStepRequisitionAsync(
        HRCoreDbContext db,
        Guid creatorId,
        int stepLevel,
        Guid? orgUnitId = null,
        string numberSuffix = "001")
    {
        var definition = await db.WorkflowDefinitions
            .FirstOrDefaultAsync(d => d.Code == "RECRUITMENT-REQUISITION-APPROVAL" && d.Version == 1);
        if (definition is null)
        {
            definition = new WorkflowDefinition
            {
                Code = "RECRUITMENT-REQUISITION-APPROVAL",
                Name = "اعتماد طلب شاغر",
                Version = 1,
                IsPublished = true,
                DomainEntityType = "RecruitmentRequisition",
                StepsJson = """
                    [
                      {"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"},
                      {"Level":2,"Name":"اعتماد إداري","StepType":0,"RoleRequired":"hr-management-approver"}
                    ]
                    """,
                GuardrailRolesJson = "[]"
            };
            db.WorkflowDefinitions.Add(definition);
            await db.SaveChangesAsync();
        }

        var org = orgUnitId is Guid scopedOrg
            ? await db.OrgUnits.FindAsync(scopedOrg)
            : new OrgUnit { Code = "OU-WF23-R", NameAr = "وحدة", NameEn = "Unit" };
        if (orgUnitId is null)
            db.OrgUnits.Add(org!);
        var position = new FundedPosition
        {
            PositionCode = $"VAC-WF23-R-{numberSuffix}",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org!.Id
        };
        db.FundedPositions.Add(position);

        var req = new RecruitmentRequisition
        {
            RequisitionNumber = $"REQ-WF23-{numberSuffix}",
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
            CurrentStepLevel = stepLevel,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = creatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (req.Id, instance.Id);
    }

    private static async Task<(Guid OfferId, Guid InstanceId)> SeedTwoStepOfferAsync(
        HRCoreDbContext db,
        Guid creatorId,
        int stepLevel)
    {
        var definition = new WorkflowDefinition
        {
            Code = "RECRUITMENT-OFFER-APPROVAL",
            Name = "اعتماد عرض",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "OfferLetter",
            StepsJson = """
                [
                  {"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"},
                  {"Level":2,"Name":"اعتماد إداري","StepType":0,"RoleRequired":"hr-management-approver"}
                ]
                """,
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var app = new RecruitmentApplication
        {
            ApplicantFullName = "مرشح",
            Status = RecruitmentApplicationStatus.OfferExtended,
            AppliedAt = DateTime.UtcNow
        };
        db.RecruitmentApplications.Add(app);
        var org = new OrgUnit { Code = "OU-WF23-O", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-WF23-O",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        await db.SaveChangesAsync();

        var offer = new OfferLetter
        {
            OfferNumber = "OFF-WF23-001",
            Status = OfferStatus.PendingApproval,
            RecruitmentApplicationId = app.Id,
            FundedPositionId = position.Id,
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
            CurrentStepLevel = stepLevel,
            Status = WorkflowStatus.InProgress,
            InitiatedBy = creatorId,
            StartedAt = DateTime.UtcNow
        };
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        return (offer.Id, instance.Id);
    }

    private static async Task<(SelectionCommitteeSession Session, WorkflowInstance Instance)> SeedCommitteeAsync(
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
            StepsJson = """[{"Level":1,"Name":"تشكيل","StepType":0,"RoleRequired":"hr-core"}]""",
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var org = new OrgUnit { Code = "OU-WF23-C", NameAr = "وحدة", NameEn = "Unit" };
        db.OrgUnits.Add(org);
        var position = new FundedPosition
        {
            PositionCode = "VAC-WF23-C",
            GradeCode = "G5",
            BudgetYear = 2026,
            Status = FundedPositionStatus.Vacant,
            OrgUnitId = org.Id
        };
        db.FundedPositions.Add(position);
        var requisition = new RecruitmentRequisition
        {
            RequisitionNumber = "REQ-WF23-C",
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
                NationalId = $"WF23C{i + 1:0000000000}",
                PrimaryOrgUnitId = org.Id
            };
            db.Employees.Add(employee);
            await db.SaveChangesAsync();
            session.Members.Add(new SelectionCommitteeMember { EmployeeId = employee.Id, Role = "عضو" });
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

    private static async Task<(RecruitmentAppeal Appeal, WorkflowInstance Instance)> SeedAppealAsync(
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
            ApplicantFullName = "مستأنف",
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

    private static async Task<Guid> SeedEstablishmentChangeAsync(
        HRCoreDbContext db,
        Guid creatorId,
        int stepLevel)
    {
        var definition = new WorkflowDefinition
        {
            Code = "ESTABLISHMENT-CHANGE-APPROVAL",
            Name = "اعتماد تغيير ملاك",
            Version = 1,
            IsPublished = true,
            DomainEntityType = "EstablishmentChangeRequest",
            StepsJson = """[{"Level":1,"Name":"مراجعة HR","StepType":0,"RoleRequired":"hr-core"}]""",
            GuardrailRolesJson = "[]"
        };
        db.WorkflowDefinitions.Add(definition);

        var request = new EstablishmentChangeRequest
        {
            RequestNumber = "EST-WF23-001",
            ChangeType = EstablishmentChangeType.CreateJobCatalog,
            Status = EstablishmentChangeStatus.PendingApproval,
            PayloadJson = "{}",
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
        return request.Id;
    }

    private static async Task<Guid> SeedLegacyApprovalAsync(
        HRCoreDbContext db,
        Guid creatorId,
        string role)
    {
        var request = new ApprovalRequest
        {
            EntityType = "ApprovedCV",
            EntityId = Guid.NewGuid(),
            Description = "اعتماد سيرة legacy",
            RequestedBy = creatorId,
            RequestedByName = "creator",
            Status = ApprovalStatus.Pending,
            CurrentLevel = 1,
            RequestedAt = DateTime.UtcNow
        };
        request.Steps.Add(new ApprovalStep
        {
            Level = 1,
            RoleRequired = role,
            Status = ApprovalStatus.Pending
        });
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private static ClaimsPrincipal CreateUser(Guid userId, string role) =>
        new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        ], "Test"));

    private static ClaimsPrincipal CreateBranchManager(Guid userId, Guid scopeOrgUnitId, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(HrOrgScopeVisibility.ScopeOrgUnitClaimType, scopeOrgUnitId.ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        claims.Add(new Claim(ClaimTypes.Role, HrManagementRoles.BranchManager));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
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
