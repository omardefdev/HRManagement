using System.Reflection;
using System.Security.Claims;
using HR.Movements.Web.Controllers;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-50 (§1, §2, P-3): the override attribute in the module filter, the approver's reach in the five movement types, the creator role
/// that does not approve, and the gateway's exact match of the movement approval paths.
/// </summary>
public class Iam050MovementApprovalPolicyTests
{
    private static readonly Type[] MovementControllers =
    [
        typeof(TransferRequestsController),
        typeof(SecondmentRequestsController),
        typeof(LoanAssignmentRequestsController),
        typeof(PromotionRequestsController),
        typeof(DelegationRequestsController)
    ];

    private static readonly string[] ReadActions = ["Index", "Details"];
    private static readonly string[] ApproveActions = ["Approve", "Reject"];

    internal static ClaimsPrincipal User(params string[] roles) => UserWith(Guid.NewGuid().ToString("D"), roles);

    internal static ClaimsPrincipal UserWith(string? tenant, params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new("azp", "hr-gateway-client")
        };
        if (tenant is not null)
            claims.Add(new Claim(TenantContext.TenantClaimType, tenant));
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    internal static IServiceProvider Services(Action<IConfigurationBuilder>? configure = null)
    {
        var configuration = new ConfigurationBuilder();
        configure?.Invoke(configuration);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration.Build());
        services.AddLogging();
        services.AddAuthorization();
        services.AddHrManagementAuthorizationPolicies();
        return services.BuildServiceProvider();
    }

    // ---- the attribute in the filter ----

    private static async Task<IActionResult?> RunFilterAsync(ClaimsPrincipal user, params object[] metadata)
    {
        var http = new DefaultHttpContext { User = user, RequestServices = Services() };
        var descriptor = new ActionDescriptor { EndpointMetadata = metadata.ToList() };
        var context = new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(new RouteValueDictionary { ["controller"] = "TransferRequests" }), descriptor),
            new List<IFilterMetadata>());
        await new HrModuleAuthorizationFilter(HrManagementPolicies.ModuleHrMovements).OnAuthorizationAsync(context);
        return context.Result;
    }

    [Fact]
    public async Task Without_the_attribute_the_module_policy_decides()
    {
        Assert.IsType<ForbidResult>(await RunFilterAsync(User(HrManagementRoles.HrManagementApprover)));
        Assert.Null(await RunFilterAsync(User(HrManagementRoles.HrMovements)));
    }

    [Fact]
    public async Task With_the_attribute_its_policy_replaces_the_module_policy()
    {
        var overrideToAccess = new ModulePolicyOverrideAttribute(HrManagementPolicies.HrMovementsApprovalAccess);
        Assert.Null(await RunFilterAsync(User(HrManagementRoles.HrManagementApprover), overrideToAccess));
        Assert.Null(await RunFilterAsync(User(HrManagementRoles.HrMovements), overrideToAccess));
        // The override widens nothing beyond its own policy.
        Assert.IsType<ForbidResult>(await RunFilterAsync(User(HrManagementRoles.HrCore), overrideToAccess));
        Assert.IsType<ForbidResult>(await RunFilterAsync(User(HrManagementRoles.HrEmployee), overrideToAccess));
    }

    [Fact]
    public async Task The_actions_attribute_wins_over_the_controllers()
    {
        // Endpoint metadata lists the controller's attributes first, then the action's.
        var controller = new ModulePolicyOverrideAttribute(HrManagementPolicies.HrMovementsApprovalAccess);
        var action = new ModulePolicyOverrideAttribute(HrManagementPolicies.HrMovementsWrite);
        Assert.IsType<ForbidResult>(await RunFilterAsync(User(HrManagementRoles.HrManagementApprover), controller, action));
        Assert.Null(await RunFilterAsync(User(HrManagementRoles.HrMovements), controller, action));
    }

    [Fact]
    public async Task An_unauthenticated_caller_is_challenged_under_the_attribute()
    {
        var result = await RunFilterAsync(new ClaimsPrincipal(new ClaimsIdentity()),
            new ModulePolicyOverrideAttribute(HrManagementPolicies.HrMovementsApprovalAccess));
        Assert.IsType<ChallengeResult>(result);
    }

    [Fact]
    public async Task An_unknown_policy_fails_the_boot()
    {
        var provider = Services().GetRequiredService<IAuthorizationPolicyProvider>();
        var known = new ActionDescriptor { EndpointMetadata = [new ModulePolicyOverrideAttribute(HrManagementPolicies.HrMovementsApprovalAccess)] };
        var unknown = new ActionDescriptor { EndpointMetadata = [new ModulePolicyOverrideAttribute("No.Such.Policy")] };

        Assert.Empty(await ModulePolicyOverrideValidator.FindUnknownPoliciesAsync([known], provider));
        Assert.Equal(["No.Such.Policy"], await ModulePolicyOverrideValidator.FindUnknownPoliciesAsync([known, unknown, unknown], provider));

        var validator = new ModulePolicyOverrideValidator(new FixedActions([known, unknown]), provider);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => validator.StartAsync(CancellationToken.None));
        Assert.Contains("No.Such.Policy", error.Message);
        await new ModulePolicyOverrideValidator(new FixedActions([known]), provider).StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_movement_service_registers_only_known_override_policies()
    {
        var provider = Services().GetRequiredService<IAuthorizationPolicyProvider>();
        var descriptors = MovementControllers.SelectMany(Actions)
            .Select(a => new ActionDescriptor { EndpointMetadata = Metadata(a.Controller, a.Method).ToList() });
        Assert.Empty(await ModulePolicyOverrideValidator.FindUnknownPoliciesAsync(descriptors, provider));
    }

    private sealed class FixedActions(IReadOnlyList<ActionDescriptor> items) : Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorCollectionProvider
    {
        public Microsoft.AspNetCore.Mvc.Infrastructure.ActionDescriptorCollection ActionDescriptors { get; } = new(items, 1);
    }

    // ---- per action and role ----

    private static IEnumerable<(Type Controller, MethodInfo Method)> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<NonActionAttribute>() is null
                        && (m.ReturnType == typeof(IActionResult) || m.ReturnType == typeof(Task<IActionResult>)))
            .Select(m => (controller, m));

    private static IEnumerable<object> Metadata(Type controller, MethodInfo method) =>
        controller.GetCustomAttributes(inherit: true).Concat(method.GetCustomAttributes(inherit: true));

    /// <summary>What the service decides: the override or the module policy in the filter, and every <c>[Authorize]</c> on top.</summary>
    private static async Task<bool> ServicePassesAsync(IAuthorizationService authorization, ClaimsPrincipal user, Type controller, MethodInfo method)
    {
        var metadata = Metadata(controller, method).ToList();
        var modulePolicy = metadata.OfType<ModulePolicyOverrideAttribute>().LastOrDefault()?.Policy ?? HrManagementPolicies.ModuleHrMovements;
        var policies = metadata.OfType<AuthorizeAttribute>().Select(a => a.Policy).OfType<string>().Append(modulePolicy);
        foreach (var policy in policies)
        {
            if (!(await authorization.AuthorizeAsync(user, new DefaultHttpContext(), policy)).Succeeded)
                return false;
        }

        return true;
    }

    public static TheoryData<string, bool, bool, bool> Personas => new()
    {
        // role, reads, approves, writes
        { HrManagementRoles.HrManagementApprover, true, true, false },
        { HrManagementRoles.HrMovements, true, false, true },
        { HrManagementRoles.Admin, true, true, true },
        { HrManagementRoles.HrCore, false, false, false },
        { HrManagementRoles.HrEmployee, false, false, false },
        { HrManagementRoles.BranchManager, false, false, false },
    };

    [Theory]
    [MemberData(nameof(Personas))]
    public async Task Each_action_admits_exactly_its_audience(string role, bool reads, bool approves, bool writes)
    {
        var authorization = Services().GetRequiredService<IAuthorizationService>();
        var user = User(role, "offline_access");
        var checkedActions = 0;
        foreach (var (controller, method) in MovementControllers.SelectMany(Actions))
        {
            var expected = ReadActions.Contains(method.Name) ? reads
                : ApproveActions.Contains(method.Name) ? approves
                : writes;
            Assert.True(expected == await ServicePassesAsync(authorization, user, controller, method),
                $"{role} on {controller.Name}.{method.Name}: expected {(expected ? "pass" : "403")}");
            checkedActions++;
        }

        Assert.Equal(41, checkedActions);
    }

    [Fact]
    public void Every_action_of_the_five_controllers_names_its_policy()
    {
        foreach (var (controller, method) in MovementControllers.SelectMany(Actions))
        {
            var metadata = Metadata(controller, method).ToList();
            var named = metadata.OfType<ModulePolicyOverrideAttribute>().Any()
                        || metadata.OfType<AuthorizeAttribute>().Any(a => a.Policy == HrManagementPolicies.HrMovementsWrite);
            Assert.True(named, $"{controller.Name}.{method.Name} carries no explicit policy");
        }
    }

    [Fact]
    public void The_approval_actions_keep_the_approve_policy_on_top_of_the_override()
    {
        foreach (var (controller, method) in MovementControllers.SelectMany(Actions).Where(a => ApproveActions.Contains(a.Method.Name)))
        {
            var metadata = Metadata(controller, method).ToList();
            Assert.Contains(metadata.OfType<AuthorizeAttribute>(), a => a.Policy == HrManagementPolicies.HrManagementApprove);
            Assert.Equal(HrManagementPolicies.HrMovementsApprovalAccess, metadata.OfType<ModulePolicyOverrideAttribute>().Single().Policy);
        }
    }

    /// <summary>PLAN-50 (§1, rule 2): the override never sits on a create, edit, execute or cancel action, in any service.</summary>
    [Fact]
    public void The_override_never_sits_on_a_write_action()
    {
        var assemblies = new[] { "HR.Movements.Web", "HR.Core.Web", "HR.Contracts.Web", "Admin.Logistics.Web", "HRManagement.Gateway" }
            .Select(Assembly.Load);
        var allowed = ReadActions.Concat(ApproveActions).ToHashSet(StringComparer.Ordinal);
        var found = 0;
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()).Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            Assert.True(type.GetCustomAttribute<ModulePolicyOverrideAttribute>() is null,
                $"{type.Name} carries the override on the controller, which would reach its write actions");
            foreach (var (_, method) in Actions(type).Where(a => a.Method.GetCustomAttribute<ModulePolicyOverrideAttribute>() is not null))
            {
                Assert.True(allowed.Contains(method.Name), $"{type.Name}.{method.Name} carries the override but is not a list, details, approve or reject action");
                Assert.True(method.GetCustomAttribute<HttpPostAttribute>() is null || ApproveActions.Contains(method.Name),
                    $"{type.Name}.{method.Name} is a POST under the override");
                found++;
            }
        }

        Assert.Equal(16, found);
    }

    [Fact]
    public void The_creator_is_always_the_caller()
    {
        // PLAN-50: a posted RequestedBy is never kept, so the creator check of the approval rests on the token (transfers did this already).
        var source = System.IO.File.ReadAllText(Path.Combine(RepoRoot(), "src", "Services", "HRMovements", "HR.Movements.Web", "Controllers", "MovementsExtendedControllers.cs"));
        Assert.DoesNotContain("RequestedBy ??=", source);
    }

    internal static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("repository root not found");
    }

    // ---- the gateway ----

    private static readonly string Id = Guid.NewGuid().ToString("D");

    [Theory]
    [InlineData("GET", "/ui/hr-movements")]
    [InlineData("GET", "/ui/hr-movements/")]
    [InlineData("GET", "/ui/hr-movements/TransferRequests")]
    [InlineData("GET", "/ui/hr-movements/transferrequests/index")]
    [InlineData("GET", "/ui/hr-movements/SecondmentRequests/Index/")]
    [InlineData("GET", "/ui/hr-movements/LoanAssignmentRequests/Details/{id}")]
    [InlineData("GET", "/ui/hr-movements/PromotionRequests/Details")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Approve/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Reject/{id}")]
    [InlineData("POST", "/ui/hr-movements/DelegationRequests/Approve/{id}")]
    [InlineData("GET", "/ui/hr-movements/css/site.css")]
    [InlineData("GET", "/ui/hr-movements/lib/bootstrap/dist/js/bootstrap.bundle.min.js")]
    [InlineData("GET", "/ui/hr-movements/_content/HRManagement.Web.Shared/hr.css")]
    [InlineData("GET", "/ui/hr-movements/HR.Movements.Web.styles.css")]
    [InlineData("GET", "/ui/hr-movements/favicon.ico")]
    public void The_named_movement_approval_paths_admit_any_staff_user(string method, string path)
    {
        Assert.True(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(
            "hr-movements", method, new PathString(path.Replace("{id}", Id)), out var policy));
        Assert.Equal(HrManagementPolicies.GatewayApprovalReach, policy);
    }

    [Theory]
    [InlineData("GET", "/ui/hr-movements/TransferRequests/Create")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Create")]
    [InlineData("GET", "/ui/hr-movements/TransferRequests/Edit/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Execute/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Cancel/{id}")]
    [InlineData("POST", "/ui/hr-movements/SecondmentRequests/Activate/{id}")]
    [InlineData("POST", "/ui/hr-movements/LoanAssignmentRequests/Close/{id}")]
    [InlineData("GET", "/ui/hr-movements/TransferRequests/Approve/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Details/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Index")]
    [InlineData("POST", "/ui/hr-movements/SecondmentRequests/Reject/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/ApproveAll")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Approve/{id}/Execute")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests/Approve/not-a-guid")]
    [InlineData("POST", "/ui/hr-movements/TransferRequestsX/Approve/{id}")]
    [InlineData("POST", "/ui/hr-movements/TransferRequests//Approve")]
    [InlineData("GET", "/ui/hr-movements/TransferRequests/Index/{id}")]
    [InlineData("POST", "/api/hr-movements/TransferRequests/Approve/{id}")]
    [InlineData("GET", "/api/hr-movements/TransferRequests/Details/{id}")]
    [InlineData("GET", "/ui/hr-movements/MovementNotices/Index")]
    [InlineData("GET", "/ui/hr-movements/TransferFinancialImpacts")]
    [InlineData("GET", "/ui/hr-movements/hubs/notifications")]
    [InlineData("GET", "/ui/hr-movements/Dashboard")]
    [InlineData("POST", "/ui/hr-movements/css/site.css")]
    [InlineData("GET", "/ui/hr-movements/css/../TransferRequests/Create")]
    [InlineData("GET", "/ui/hr-movements/sub/HR.Movements.Web.styles.css")]
    public void Every_other_movement_path_keeps_the_module_policy(string method, string path)
    {
        Assert.False(GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(
            "hr-movements", method, new PathString(path.Replace("{id}", Id)), out _));
    }

    private static async Task<int> GatewayAsync(string method, string path, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user, RequestServices = Services() };
        http.Request.Method = method;
        http.Request.Path = path.Replace("{id}", Id);
        var next = false;
        var middleware = new GatewayPathAuthorizationMiddleware(_ =>
        {
            next = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(http, http.RequestServices.GetRequiredService<IAuthorizationService>());
        return next ? 200 : http.Response.StatusCode;
    }

    [Fact]
    public async Task At_the_gateway_the_approver_reaches_the_approval_screens_and_no_write()
    {
        var approver = User(HrManagementRoles.HrManagementApprover, "offline_access");
        Assert.Equal(200, await GatewayAsync("GET", "/ui/hr-movements/TransferRequests", approver));
        Assert.Equal(200, await GatewayAsync("GET", "/ui/hr-movements/TransferRequests/Details/{id}", approver));
        Assert.Equal(200, await GatewayAsync("POST", "/ui/hr-movements/PromotionRequests/Approve/{id}", approver));
        Assert.Equal(403, await GatewayAsync("POST", "/ui/hr-movements/TransferRequests/Create", approver));
        Assert.Equal(403, await GatewayAsync("POST", "/ui/hr-movements/TransferRequests/Execute/{id}", approver));
        Assert.Equal(403, await GatewayAsync("GET", "/ui/hr-movements/MovementNotices", approver));

        // The creator role keeps its paths; the gateway lets it reach approve, and the service refuses it (HrManagement.Approve).
        var movements = User(HrManagementRoles.HrMovements, "offline_access");
        Assert.Equal(200, await GatewayAsync("POST", "/ui/hr-movements/TransferRequests/Create", movements));
        Assert.Equal(200, await GatewayAsync("POST", "/ui/hr-movements/TransferRequests/Approve/{id}", movements));

        // A non-staff user and Keycloak's own roles reach nothing.
        Assert.Equal(403, await GatewayAsync("GET", "/ui/hr-movements/TransferRequests", User(HrManagementRoles.HrEmployee, "offline_access")));
        Assert.Equal(403, await GatewayAsync("GET", "/ui/hr-movements/TransferRequests", User("offline_access", "uma_authorization", "default-roles-hrmanagement")));
    }

    // ---- Gateway.ApprovalReach ----

    private static async Task<bool> ReachAsync(ClaimsPrincipal user, IServiceProvider? services = null) =>
        (await (services ?? Services()).GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(user, HrManagementPolicies.GatewayApprovalReach)).Succeeded;

    [Fact]
    public async Task Approval_reach_admits_an_interactive_staff_user_with_a_tenant()
    {
        Assert.True(await ReachAsync(User(HrManagementRoles.HrManagementApprover, "offline_access")));
        Assert.True(await ReachAsync(User(HrManagementRoles.HrCore)));
        Assert.True(await ReachAsync(User(HrManagementRoles.AdminLogistics)));
        Assert.True(await ReachAsync(User(HrManagementRoles.HrEmployee, HrManagementRoles.HrMovements)));
    }

    [Fact]
    public async Task Approval_reach_counts_only_the_platforms_own_staff_roles()
    {
        // The prompt's case: a self-service user who, like every user, also carries Keycloak's offline_access.
        Assert.False(await ReachAsync(User(HrManagementRoles.HrEmployee, "offline_access")));
        Assert.False(await ReachAsync(User("offline_access", "uma_authorization", "default-roles-hrmanagement")));
        Assert.False(await ReachAsync(User(HrManagementRoles.HrExamMarker, "offline_access")));
        Assert.False(await ReachAsync(User("applicant")));
        Assert.False(await ReachAsync(User()));
    }

    [Fact]
    public async Task Approval_reach_refuses_a_service_account_and_a_missing_or_invalid_tenant()
    {
        var service = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("azp", ServiceClientIds.HrMovements),
            new Claim(TenantContext.TenantClaimType, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, HrManagementRoles.HrCore)
        ], "TestAuth"));
        Assert.False(await ReachAsync(service));
        Assert.False(await ReachAsync(UserWith(null, HrManagementRoles.HrManagementApprover)));
        Assert.False(await ReachAsync(UserWith("not-a-guid", HrManagementRoles.HrManagementApprover)));
        Assert.False(await ReachAsync(UserWith(Guid.Empty.ToString("D"), HrManagementRoles.HrManagementApprover)));
        Assert.False(await ReachAsync(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Fact]
    public async Task The_non_staff_list_comes_from_configuration()
    {
        var services = Services(c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authorization:NonStaffRoles:0"] = HrManagementRoles.HrEmployee,
            ["Authorization:NonStaffRoles:1"] = HrManagementRoles.HrCore
        }));
        Assert.False(await ReachAsync(User(HrManagementRoles.HrCore), services));
        Assert.True(await ReachAsync(User(HrManagementRoles.HrManagementApprover), services));
        Assert.Equal(GatewayApprovalReachHandler.DefaultNonStaffRoles, GatewayApprovalReachHandler.NonStaffRoles(null));
    }
}
