using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.Controllers;
using HR.Core.Web.Services;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace HRManagement.UnitTests;

/// <summary>PLAN-45: who reads and who decides on the generic approval page.</summary>
public sealed class Wf045WorkflowApprovalsControllerTests
{
    private static readonly Guid Requester = Guid.Parse("45450000-0000-4000-8000-0000000000a1");
    private static readonly Guid Officer = Guid.Parse("45450000-0000-4000-8000-0000000000a2");

    private static ClaimsPrincipal User(Guid id, params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r)).Prepend(new Claim(ClaimTypes.NameIdentifier, id.ToString("D"))),
        "test", ClaimTypes.Name, ClaimTypes.Role));

    private static async Task<(HRCoreDbContext Db, WorkflowInstance Instance)> ArrangeAsync(
        string entityType = "AssetAssignment", string code = "ASSET-ASSIGN-APPROVAL", Action<WorkflowInstance>? change = null)
    {
        var db = new HRCoreDbContext(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        var definition = await db.WorkflowDefinitions.AsNoTracking()
            .Where(d => d.Code == code && d.IsPublished).OrderByDescending(d => d.Version).FirstAsync();
        var instance = new WorkflowInstance
        {
            TenantId = TenantDefaults.DefaultTenantId,
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = entityType,
            EntityId = Guid.NewGuid(),
            Status = WorkflowStatus.InProgress,
            CurrentStepLevel = 1,
            InitiatedBy = Requester,
            RequestedByUserId = Requester,
            RequestedByName = "الطالب",
            DisplayTitle = "تسليم عهدة TAG-1",
            DetailsUrl = "/ui/admin-logistics/Assets/Details/00000000-0000-0000-0000-000000000001",
            ContextJson = JsonSerializer.Serialize(new Dictionary<string, string> { ["tagNumber"] = "<script>alert(1)</script>" }),
            ExcludedApproverUserIdsJson = JsonSerializer.Serialize(new[] { Requester }),
            StartedAt = DateTime.UtcNow
        };
        change?.Invoke(instance);
        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, instance);
    }

    private static WorkflowApprovalsController Controller(HRCoreDbContext db, ClaimsPrincipal user, IWorkflowInstanceActionService? actions = null)
    {
        var scope = new WorkflowGateOrgScope(new OrgScopeService(db, new OrgUnitHierarchyCache(new MemoryCache(new MemoryCacheOptions()))));
        var http = new DefaultHttpContext { User = user };
        return new WorkflowApprovalsController(db, actions ?? new RefusingActions(), WorkflowApprovalEvaluator.CreateDefault(db), scope)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new NoTempData())
        };
    }

    [Theory]
    [InlineData(HrManagementRoles.AdminLogistics)]
    [InlineData(HrManagementRoles.HrManagementApprover)]
    [InlineData(HrManagementRoles.Admin)]
    public async Task A_step_role_or_admin_reads_the_page(string role)
    {
        var (db, instance) = await ArrangeAsync();
        await using var _ = db;

        var view = Assert.IsType<ViewResult>(await Controller(db, User(Officer, role)).Details(instance.Id, default));

        var model = Assert.IsType<WorkflowApprovalDetailsViewModel>(view.Model);
        Assert.Equal("تسليم عهدة TAG-1", model.Title);
        Assert.Equal("<script>alert(1)</script>", model.Facts["tagNumber"]);
    }

    [Fact]
    public async Task The_requester_reads_their_request_and_is_offered_no_decision()
    {
        var (db, instance) = await ArrangeAsync();
        await using var _ = db;

        var view = Assert.IsType<ViewResult>(await Controller(db, User(Requester, HrManagementRoles.AdminLogistics)).Details(instance.Id, default));

        var model = Assert.IsType<WorkflowApprovalDetailsViewModel>(view.Model);
        Assert.False(model.Panel.CanApprove);
        Assert.False(model.Panel.CanReject);
        Assert.Equal(WorkflowSodRules.ExcludedMessage, model.Panel.BlockReason);
    }

    [Fact]
    public async Task The_page_offers_what_the_evaluator_allows()
    {
        var (db, instance) = await ArrangeAsync();
        await using var _ = db;

        var view = Assert.IsType<ViewResult>(await Controller(db, User(Officer, HrManagementRoles.AdminLogistics)).Details(instance.Id, default));

        var model = Assert.IsType<WorkflowApprovalDetailsViewModel>(view.Model);
        Assert.True(model.Panel.CanApprove);
        Assert.True(model.Panel.CanReject);
        Assert.Equal(1, model.Panel.ApproveTargetStepLevel);
    }

    [Theory]
    [InlineData(HrManagementRoles.HrCore)]
    [InlineData(HrManagementRoles.BranchManager)]
    [InlineData(HrManagementRoles.Compliance)]
    public async Task Holding_the_inbox_opens_no_instance_by_its_id(string role)
    {
        var (db, instance) = await ArrangeAsync();
        await using var _ = db;

        Assert.IsType<ForbidResult>(await Controller(db, User(Officer, role)).Details(instance.Id, default));
        Assert.IsType<ForbidResult>(await Controller(db, User(Officer, role)).ApproveWorkflow(instance.Id, null, 1, default));
    }

    [Fact]
    public async Task A_type_not_registered_answers_404()
    {
        var (db, instance) = await ArrangeAsync("RecruitmentRequisition", "RECRUITMENT-REQUISITION-APPROVAL");
        await using var _ = db;
        var user = User(Officer, HrManagementRoles.Admin);

        Assert.IsType<NotFoundResult>(await Controller(db, user).Details(instance.Id, default));
        Assert.IsType<NotFoundResult>(await Controller(db, user).ApproveWorkflow(instance.Id, null, 1, default));
        Assert.IsType<NotFoundResult>(await Controller(db, user).RejectWorkflow(instance.Id, "سبب", 1, default));
    }

    [Fact]
    public async Task An_unknown_instance_answers_404()
    {
        var (db, _) = await ArrangeAsync();
        await using var __ = db;

        Assert.IsType<NotFoundResult>(await Controller(db, User(Officer, HrManagementRoles.Admin)).Details(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task A_link_that_fails_the_prefix_is_not_shown()
    {
        var (db, instance) = await ArrangeAsync(change: i => i.DetailsUrl = "//evil.example/x");
        await using var _ = db;

        var view = Assert.IsType<ViewResult>(await Controller(db, User(Officer, HrManagementRoles.AdminLogistics)).Details(instance.Id, default));

        Assert.Null(Assert.IsType<WorkflowApprovalDetailsViewModel>(view.Model).DetailsUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_rejection_needs_a_reason(string? comments)
    {
        var (db, instance) = await ArrangeAsync();
        await using var _ = db;
        var actions = new RefusingActions();
        var controller = Controller(db, User(Officer, HrManagementRoles.AdminLogistics), actions);

        Assert.IsType<RedirectToActionResult>(await controller.RejectWorkflow(instance.Id, comments, 1, default));

        Assert.Equal(WorkflowApprovalsController.RejectReasonMessage, controller.TempData["Error"]);
        Assert.Equal(0, actions.Calls);
    }

    [Fact]
    public async Task The_decision_goes_to_the_same_service_as_the_api_with_the_step_the_page_showed()
    {
        var (db, instance) = await ArrangeAsync();
        await using var _ = db;
        var actions = new RefusingActions();
        var controller = Controller(db, User(Officer, HrManagementRoles.AdminLogistics), actions);

        await controller.ApproveWorkflow(instance.Id, "موافق", 1, default);

        Assert.Equal(1, actions.Calls);
        Assert.Equal(1, actions.Step);
        Assert.Equal(Officer, actions.Actor!.UserId);
        Assert.Equal(RefusingActions.Refusal, controller.TempData["Error"]);
    }

    [Fact]
    public void Each_post_demands_the_policy_and_the_token_and_the_class_the_inbox()
    {
        var type = typeof(WorkflowApprovalsController);
        Assert.Equal(HrManagementPolicies.HrCoreWorkInbox, type.GetCustomAttribute<AuthorizeAttribute>()!.Policy);

        foreach (var name in new[] { nameof(WorkflowApprovalsController.ApproveWorkflow), nameof(WorkflowApprovalsController.RejectWorkflow) })
        {
            var method = type.GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
            Assert.NotNull(method.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
            Assert.Equal(HrManagementPolicies.WorkflowStepAction, method.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        }
    }

    [Theory]
    [InlineData("/ui/hr-core/WorkflowApprovals/Details/00000000-0000-0000-0000-000000000001", "GET", HrManagementPolicies.HrCoreWorkInbox)]
    [InlineData("/ui/hr-core/WorkflowApprovals/ApproveWorkflow/00000000-0000-0000-0000-000000000001", "POST", HrManagementPolicies.GatewayApprovalReach)]
    [InlineData("/ui/hr-core/WorkflowApprovals/RejectWorkflow/00000000-0000-0000-0000-000000000001", "POST", HrManagementPolicies.GatewayApprovalReach)]
    // PLAN-50: any other POST under the page keeps the step policy.
    [InlineData("/ui/hr-core/WorkflowApprovals/Other/00000000-0000-0000-0000-000000000001", "POST", HrManagementPolicies.WorkflowStepAction)]
    public void The_gateway_applies_the_same_policies(string path, string method, string expected)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("hr-core", method, new PathString(path), out var policy));
        Assert.Equal(expected, policy);
    }

    [Fact]
    public async Task The_module_filter_lets_the_controller_decide()
    {
        var context = new AuthorizationFilterContext(
            new ActionContext(
                new DefaultHttpContext { User = User(Officer, HrManagementRoles.AdminLogistics) },
                new RouteData(new RouteValueDictionary { ["controller"] = "WorkflowApprovals" }),
                new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()),
            []);

        await new HrModuleAuthorizationFilter(HrManagementPolicies.ModuleHrCore).OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    // The inbox.

    [Fact]
    public void The_audience_comes_from_the_registry()
    {
        // PLAN-46 added the card office and compliance.
        Assert.Equal([HrManagementRoles.AdminLogistics, HrManagementRoles.IdCard, HrManagementRoles.Compliance], WorkInboxAudience.Roles);
        Assert.Equal(
            ExternalApprovalTypes.All.Where(t => t.AudienceRoles.Contains(HrManagementRoles.AdminLogistics)).Select(t => t.EntityType),
            WorkInboxAudience.EntityTypesFor(User(Officer, HrManagementRoles.AdminLogistics)));
    }

    [Theory]
    [InlineData(true, HrManagementRoles.AdminLogistics)]
    [InlineData(false, HrManagementRoles.AdminLogistics, HrManagementRoles.HrCore)]
    [InlineData(false, HrManagementRoles.AdminLogistics, HrManagementRoles.BranchManager)]
    [InlineData(false, HrManagementRoles.HrManagementApprover)]
    [InlineData(true, HrManagementRoles.Compliance)]
    [InlineData(true, HrManagementRoles.IdCard)]
    [InlineData(false, HrManagementRoles.Compliance, HrManagementRoles.HrCore)]
    public void Only_a_role_with_no_hr_core_access_is_audience_only(bool expected, params string[] roles) =>
        Assert.Equal(expected, WorkInboxAudience.IsAudienceOnly(User(Officer, roles)));

    private sealed class RefusingActions : IWorkflowInstanceActionService
    {
        public const string Refusal = "مرفوض";

        public int Calls { get; private set; }

        public int? Step { get; private set; }

        public WorkflowActionActor? Actor { get; private set; }

        public Task<WorkflowActionResult> ApproveAsync(Guid instanceId, WorkflowActionActor actor, string? comments, int? targetStepLevel, CancellationToken ct = default)
        {
            Calls++;
            Step = targetStepLevel;
            Actor = actor;
            return Task.FromResult(new WorkflowActionResult(false, Refusal, null));
        }

        public Task<WorkflowActionResult> RejectAsync(Guid instanceId, WorkflowActionActor actor, string? comments, CancellationToken ct = default, int? targetStepLevel = null)
        {
            Calls++;
            Step = targetStepLevel;
            Actor = actor;
            return Task.FromResult(new WorkflowActionResult(false, Refusal, null));
        }

        public Task ReapplyOutcomeAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
