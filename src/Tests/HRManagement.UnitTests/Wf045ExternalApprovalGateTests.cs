using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.External;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

/// <summary>PLAN-45 (D-WF-2): the chain, the gate and the role that reaches it.</summary>
public sealed class Wf045ExternalApprovalGateTests
{
    private static readonly Guid Requester = Guid.Parse("45450000-0000-4000-8000-000000000001");
    private static readonly Guid Officer = Guid.Parse("45450000-0000-4000-8000-000000000002");
    private static readonly Guid Manager = Guid.Parse("45450000-0000-4000-8000-000000000003");
    private static readonly Guid SubjectEmployee = Guid.Parse("45450000-0000-4000-8000-0000000000e1");

    private static IReadOnlySet<string> Roles(params string[] roles) => roles.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static WorkflowActionActor Logistics(Guid user, Guid? employee = null) =>
        new(user, "logistics", Roles(HrManagementRoles.AdminLogistics), employee);

    private static WorkflowActionActor Approver(Guid user) =>
        new(user, "approver", Roles(HrManagementRoles.HrManagementApprover));

    private static async Task<WorkflowInstance> CustodyAsync(
        Hcm025ApprovalGatesTests.Fx fx, string code, string entityType, Guid? scopeOrgUnit = null)
    {
        await WorkflowPackSeeder.EnsureCustodyChainAsync(fx.Db);
        var definition = await fx.Db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == code && d.IsPublished);
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
            ScopeOrgUnitId = scopeOrgUnit,
            SubjectEmployeeId = SubjectEmployee,
            ExcludedApproverUserIdsJson = JsonSerializer.Serialize(new[] { Requester }),
            StartedAt = DateTime.UtcNow
        };
        fx.Db.WorkflowInstances.Add(instance);
        await fx.Db.SaveChangesAsync();
        fx.Db.ChangeTracker.Clear();
        return instance;
    }

    [Fact]
    public void Every_registered_type_has_a_gate_in_the_set() =>
        Assert.All(ExternalApprovalTypes.All, type => Assert.Contains(
            WorkflowApprovalEvaluator.CreateGates(null!, null!),
            gate => gate is ExternalApprovalGate && gate.EntityType == type.EntityType));

    [Theory]
    [InlineData("ASSET-ASSIGN-APPROVAL", "AssetAssignment")]
    [InlineData("ASSET-LOSS-APPROVAL", "AssetLossReport")]
    public async Task The_chain_is_the_officer_then_the_manager(string code, string entityType)
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await CustodyAsync(fx, code, entityType);

        Assert.Null(await fx.BlockAsync(instance, Logistics(Officer), WorkflowActionKind.Approve));
        Assert.Equal(
            WorkflowStageGuard.StepRoleMessage(HrManagementRoles.AdminLogistics),
            await fx.BlockAsync(instance, new WorkflowActionActor(Manager, "core", Roles(HrManagementRoles.HrCore)), WorkflowActionKind.Approve));
        Assert.Equal(
            WorkflowStageGuard.StepRoleMessage(HrManagementRoles.AdminLogistics),
            await fx.BlockAsync(instance, Approver(Manager), WorkflowActionKind.Approve));

        await fx.ApprovedStepAsync(instance, Officer);

        Assert.Null(await fx.BlockAsync(instance, Approver(Manager), WorkflowActionKind.Approve));
        Assert.Equal(
            WorkflowStageGuard.StepRoleMessage(HrManagementRoles.HrManagementApprover),
            await fx.BlockAsync(instance, Logistics(Manager), WorkflowActionKind.Approve));
    }

    [Theory]
    [InlineData(WorkflowActionKind.Approve)]
    [InlineData(WorkflowActionKind.Reject)]
    public async Task The_requester_acts_on_no_step(WorkflowActionKind kind)
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await CustodyAsync(fx, "ASSET-LOSS-APPROVAL", "AssetLossReport");

        Assert.Equal(WorkflowSodRules.ExcludedMessage, await fx.BlockAsync(instance, Logistics(Requester), kind));
    }

    [Fact]
    public async Task The_employee_concerned_does_not_approve_their_own_custody()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await CustodyAsync(fx, "ASSET-ASSIGN-APPROVAL", "AssetAssignment");

        Assert.Equal(
            WorkflowSodRules.SubjectMessage,
            await fx.BlockAsync(instance, Logistics(Officer, SubjectEmployee), WorkflowActionKind.Approve));
    }

    [Theory]
    [InlineData("ASSET-ASSIGN-APPROVAL", "AssetAssignment")]
    [InlineData("ASSET-LOSS-APPROVAL", "AssetLossReport")]
    public async Task One_person_with_both_roles_takes_one_step(string code, string entityType)
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await CustodyAsync(fx, code, entityType);
        var both = new WorkflowActionActor(Officer, "both", Roles(HrManagementRoles.AdminLogistics, HrManagementRoles.HrManagementApprover));

        await fx.ApprovedStepAsync(instance, Officer);

        Assert.Equal(WorkflowSodRules.DistinctApproverMessage, await fx.BlockAsync(instance, both, WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task An_admin_needs_the_step_role()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var instance = await CustodyAsync(fx, "ASSET-LOSS-APPROVAL", "AssetLossReport");

        Assert.Equal(
            WorkflowStageGuard.StepRoleMessage(HrManagementRoles.AdminLogistics),
            await fx.BlockAsync(instance, new WorkflowActionActor(Officer, "admin", Roles(HrManagementRoles.Admin)), WorkflowActionKind.Approve));
    }

    [Fact]
    public async Task A_branch_level_officer_acts_only_inside_the_unit_of_the_request()
    {
        await using var fx = await Hcm025ApprovalGatesTests.Fx.CreateAsync();
        var inside = await CustodyAsync(fx, "ASSET-ASSIGN-APPROVAL", "AssetAssignment", fx.Branch1);
        var outside = await CustodyAsync(fx, "ASSET-LOSS-APPROVAL", "AssetLossReport", fx.Branch2);
        var noUnit = await CustodyAsync(fx, "ASSET-LOSS-APPROVAL", "AssetLossReport");
        var branch = new WorkflowActionActor(
            Officer, "branch", Roles(HrManagementRoles.BranchManager, HrManagementRoles.AdminLogistics), ScopeOrgUnitClaim: fx.Branch1.ToString("D"));

        Assert.Null(await fx.BlockAsync(inside, branch, WorkflowActionKind.Approve));
        Assert.Equal(WorkflowGateOrgScope.OutOfBranchMessage, await fx.BlockAsync(outside, branch, WorkflowActionKind.Approve));
        Assert.Equal(WorkflowGateOrgScope.OutOfBranchMessage, await fx.BlockAsync(outside, branch, WorkflowActionKind.Reject));
        Assert.Equal(WorkflowGateOrgScope.CentralOnlyMessage, await fx.BlockAsync(noUnit, branch, WorkflowActionKind.Approve));
        Assert.Null(await fx.BlockAsync(outside, Logistics(Manager), WorkflowActionKind.Approve));
    }

    // The definitions.

    private static HRCoreDbContext NewDb() =>
        new(new DbContextOptionsBuilder<HRCoreDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IReadOnlyList<string> RolesOf(WorkflowDefinition definition) =>
        JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(definition.StepsJson)!.OrderBy(s => s.Level).Select(s => s.RoleRequired!).ToList();

    [Theory]
    [InlineData("ASSET-ASSIGN-APPROVAL", "AssetAssignment")]
    [InlineData("ASSET-LOSS-APPROVAL", "AssetLossReport")]
    public async Task Both_codes_rise_once_to_the_chain_and_the_old_version_retires(string code, string entityType)
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);

        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);

        var versions = await db.WorkflowDefinitions.AsNoTracking().Where(d => d.Code == code).OrderBy(d => d.Version).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsPublished);
        Assert.Equal(WorkflowDefinitionStatus.Retired, versions[0].Status);
        var latest = versions[1];
        Assert.True(latest.IsPublished);
        Assert.Equal("seed:PLAN-45", latest.ChangeReason);
        Assert.True(latest.RequireDistinctApprovers);
        Assert.Equal(entityType, latest.DomainEntityType);
        Assert.Equal([HrManagementRoles.AdminLogistics, HrManagementRoles.HrManagementApprover], RolesOf(latest));
        Assert.Empty(WorkflowDefinitionValidator.Validate(
            new WorkflowDefinitionDraft(
                latest.Code, latest.Name, latest.DomainEntityType!,
                JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(latest.StepsJson)!, [], latest.RequireDistinctApprovers),
            WorkflowDefinitionCatalog.KnownRoles,
            WorkflowDefinitionCatalog.KnownEntityTypes));
    }

    [Fact]
    public async Task A_published_v1_left_beside_a_later_chain_is_retired_and_never_published_again()
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        var v1 = await db.WorkflowDefinitions.SingleAsync(d => d.Code == "ASSET-LOSS-APPROVAL" && d.Version == 1);
        v1.IsPublished = true;
        v1.Status = WorkflowDefinitionStatus.Published;
        await db.SaveChangesAsync();

        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);

        Assert.Equal(2, (await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == "ASSET-LOSS-APPROVAL" && d.IsPublished)).Version);
        Assert.Equal(2, await db.WorkflowDefinitions.AsNoTracking().CountAsync(d => d.Code == "ASSET-LOSS-APPROVAL"));
    }

    [Fact]
    public async Task A_later_version_with_the_same_roles_is_left_alone()
    {
        await using var db = NewDb();
        await WorkflowPackSeeder.SeedAsync(db);
        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);
        var renamed = await db.WorkflowDefinitions.SingleAsync(d => d.Code == "ASSET-LOSS-APPROVAL" && d.IsPublished);
        var steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(renamed.StepsJson)!;
        renamed.StepsJson = JsonSerializer.Serialize(steps.Select(s => s with { Name = "اسم أحدث" }));
        await db.SaveChangesAsync();

        await WorkflowPackSeeder.EnsureCustodyChainAsync(db);

        Assert.Equal(2, await db.WorkflowDefinitions.AsNoTracking().CountAsync(d => d.Code == "ASSET-LOSS-APPROVAL"));
    }

    [Theory]
    [InlineData("AssetAssignment")]
    [InlineData("AssetLossReport")]
    public void The_guardrail_holds_the_chain(string entityType)
    {
        var guardrail = WorkflowGuardrails.For(entityType);

        Assert.Equal(2, guardrail.MinSteps);
        Assert.Equal(2, guardrail.MaxSteps);
        Assert.Equal(HrManagementRoles.AdminLogistics, guardrail.FirstStepRole);
        Assert.Equal(HrManagementRoles.HrManagementApprover, guardrail.LastStepRole);
        Assert.True(guardrail.RequiresDistinctApprovers);
    }

    [Fact]
    public void A_definition_outside_the_chain_is_refused()
    {
        WorkflowStepSnapshot Step(int level, string role) => new() { Level = level, Name = "خطوة", RoleRequired = role };
        IReadOnlyList<string> Validate(params WorkflowStepSnapshot[] steps) => WorkflowDefinitionValidator.Validate(
            new WorkflowDefinitionDraft("ASSET-LOSS-APPROVAL", "اسم", "AssetLossReport", steps, [], true),
            WorkflowDefinitionCatalog.KnownRoles,
            WorkflowDefinitionCatalog.KnownEntityTypes);

        Assert.Empty(Validate(Step(1, HrManagementRoles.AdminLogistics), Step(2, HrManagementRoles.HrManagementApprover)));
        Assert.Contains(
            $"الخطوة الأولى لهذا النوع يجب أن تكون بدور «{HrManagementRoles.AdminLogistics}».",
            Validate(Step(1, HrManagementRoles.HrCore), Step(2, HrManagementRoles.HrManagementApprover)));
        Assert.Contains(
            "هذا النوع يتطلب 2 خطوات على الأكثر.",
            Validate(Step(1, HrManagementRoles.AdminLogistics), Step(2, HrManagementRoles.HrCore), Step(3, HrManagementRoles.HrManagementApprover)));
    }

    // The role.

    private static IAuthorizationService Authorization()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal User(params string[] roles) => new(new ClaimsIdentity(
        roles.Select(r => new Claim(ClaimTypes.Role, r)).Prepend(new Claim(ClaimTypes.NameIdentifier, Officer.ToString("D"))), "test"));

    [Theory]
    [InlineData(HrManagementPolicies.WorkflowStepAction, true)]
    [InlineData(HrManagementPolicies.HrCoreWorkInbox, true)]
    [InlineData(HrManagementPolicies.ModuleHrCore, false)]
    [InlineData(HrManagementPolicies.HrCoreReadOnly, false)]
    [InlineData(HrManagementPolicies.HrCoreWrite, false)]
    [InlineData(HrManagementPolicies.HrCoreApprove, false)]
    [InlineData(HrManagementPolicies.HrCoreBranchOperation, false)]
    public async Task The_logistics_role_reaches_two_policies_of_hr_core_and_no_other(string policy, bool allowed) =>
        Assert.Equal(allowed, (await Authorization().AuthorizeAsync(User(HrManagementRoles.AdminLogistics), policy)).Succeeded);

    [Theory]
    [InlineData("/ui/hr-core/Employees")]
    [InlineData("/ui/hr-core/Employees/Details/00000000-0000-0000-0000-000000000001")]
    [InlineData("/ui/hr-core/Approvals")]
    [InlineData("/ui/hr-core/Reports")]
    [InlineData("/ui/hr-core/WorkflowDefinitions")]
    public async Task The_gateway_refuses_the_logistics_role_on_the_rest_of_hr_core(string path)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy("hr-core", HttpMethods.Get, new PathString(path), out var policy));
        Assert.False((await Authorization().AuthorizeAsync(User(HrManagementRoles.AdminLogistics), policy!)).Succeeded);
    }
}
