using System.Reflection;
using System.Security.Claims;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests.Architecture;

/// <summary>
/// PLAN-50 (§3, P-4): by reflection over HR.Core.Web and HR.Movements.Web. Every action whose policy is an approval policy has a gateway
/// path that a principal holding <c>hr-management-approver</c> alone passes; a principal holding <c>hr-core-supervisory</c> alone is refused
/// on the same POST paths (by the gateway, or by the service's explicit policy where the gateway admits every staff user, D-50-3); every
/// path the gateway opens to <see cref="HrManagementPolicies.GatewayApprovalReach"/> or names in <see cref="GatewayHrCoreActionPolicies"/>
/// is a real action that carries an explicit policy of its own; and no table row opens an action the service guards with another policy.
/// </summary>
public sealed class GatewayApproverReachTests
{
    private static readonly string[] ApprovalPolicies =
    [
        HrManagementPolicies.HrCoreApprove,
        HrManagementPolicies.PolicyApprover,
        HrManagementPolicies.HrManagementApprove,
        HrManagementPolicies.ManageDelegations,
        HrManagementPolicies.HrMovementsApprovalAccess
    ];

    private static readonly Guid Id = Guid.Parse("11111111-2222-4333-8444-555555555555");

    private sealed record Endpoint(
        string Module, Type Controller, MethodInfo Action, string Method, string Path, IReadOnlyList<string> Policies, string? Override)
    {
        public string ControllerName => Controller.Name[..^"Controller".Length];
        public override string ToString() => $"{Method} {Path} ({Controller.Name}.{Action.Name})";
    }

    private static readonly (Assembly Assembly, string Module, string ModulePolicy)[] Services =
    [
        (typeof(HR.Core.Web.Controllers.DelegationsController).Assembly, "hr-core", HrManagementPolicies.ModuleHrCore),
        (typeof(HR.Movements.Web.Controllers.TransferRequestsController).Assembly, "hr-movements", HrManagementPolicies.ModuleHrMovements)
    ];

    private static IReadOnlyList<Endpoint> AllEndpoints() =>
        Services.SelectMany(s => s.Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.Name.EndsWith("Controller", StringComparison.Ordinal))
                .SelectMany(c => ActionsOf(c).Select(m => ToEndpoint(s.Module, c, m))))
            .ToList();

    private static IEnumerable<MethodInfo> ActionsOf(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName
                        && m.GetCustomAttribute<NonActionAttribute>() is null
                        && m.GetBaseDefinition().DeclaringType == m.DeclaringType);

    private static Endpoint ToEndpoint(string module, Type controller, MethodInfo action)
    {
        var verbAttribute = action.GetCustomAttributes<HttpMethodAttribute>().FirstOrDefault();
        var method = verbAttribute?.HttpMethods.First() ?? HttpMethods.Get;
        var classRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template;
        var actionTemplate = verbAttribute?.Template ?? action.GetCustomAttribute<RouteAttribute>()?.Template;
        var name = controller.Name[..^"Controller".Length];

        string path;
        if (classRoute is not null || actionTemplate is not null)
        {
            var template = string.Join('/', new[] { classRoute, actionTemplate }.Where(t => !string.IsNullOrEmpty(t)));
            template = template.Replace("[controller]", name, StringComparison.Ordinal).Replace("[action]", action.Name, StringComparison.Ordinal);
            path = $"/api/{module}/" + string.Join('/', template.Split('/').Select(s => s.StartsWith('{') ? Id.ToString("D") : s));
        }
        else
        {
            var hasId = action.GetParameters().Any(p => string.Equals(p.Name, "id", StringComparison.OrdinalIgnoreCase));
            path = $"/ui/{module}/{name}/{action.Name}" + (hasId ? "/" + Id.ToString("D") : string.Empty);
        }

        var attributes = controller.GetCustomAttributes(inherit: true).Concat(action.GetCustomAttributes(inherit: true)).ToList();
        return new Endpoint(
            module, controller, action, method, path,
            attributes.OfType<AuthorizeAttribute>().Select(a => a.Policy).OfType<string>().ToList(),
            attributes.OfType<ModulePolicyOverrideAttribute>().LastOrDefault()?.Policy);
    }

    private static IReadOnlyList<Endpoint> ApprovalEndpoints() =>
        AllEndpoints().Where(e => e.Policies.Append(e.Override).Any(p => p is not null && ApprovalPolicies.Contains(p))).ToList();

    private static ClaimsPrincipal Principal(params string[] roles) =>
        Iam050MovementApprovalPolicyTests.User([.. roles, "offline_access"]);

    private static async Task<bool> GatewayPassesAsync(IServiceProvider services, Endpoint endpoint, ClaimsPrincipal user)
    {
        var http = new DefaultHttpContext { User = user, RequestServices = services };
        http.Request.Method = endpoint.Method;
        http.Request.Path = endpoint.Path;
        var passed = false;
        await new GatewayPathAuthorizationMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        }).InvokeAsync(http, services.GetRequiredService<IAuthorizationService>());
        return passed;
    }

    /// <summary>The service's own decision: the action's and the class's policies, and the override when there is one.</summary>
    private static async Task<bool> ServicePassesAsync(IServiceProvider services, Endpoint endpoint, ClaimsPrincipal user)
    {
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var policies = endpoint.Override is null ? endpoint.Policies : endpoint.Policies.Append(endpoint.Override);
        foreach (var policy in policies)
        {
            if (!(await authorization.AuthorizeAsync(user, new DefaultHttpContext(), policy)).Succeeded)
                return false;
        }

        return policies.Any();
    }

    [Fact]
    public void The_inventory_sees_the_approval_actions_of_both_services()
    {
        var endpoints = ApprovalEndpoints();
        var names = endpoints.Select(e => $"{e.ControllerName}.{e.Action.Name}").ToHashSet();
        foreach (var expected in new[]
                 {
                     // PLAN-51 (§5): onboarding left the approvals; it is the platform administrator's alone (Iam051OnboardingAndServiceAccountTests).
                     "EmploymentRecords.Approve", "EmployeeCorrections.Approve", "EmployeeCorrections.ApproveApi", "Competitions.Finalize",
                     "Competitions.Cancel", "Approvals.Approve", "Approvals.Reject",
                     "Delegations.Create", "Delegations.Revoke", "PolicyTemplates.PublishFromUi", "Policies.Publish",
                     "TransferRequests.Approve", "TransferRequests.Reject", "SecondmentRequests.Approve", "LoanAssignmentRequests.Approve",
                     "PromotionRequests.Approve", "DelegationRequests.Approve", "TransferRequests.Index", "DelegationRequests.Details"
                 })
            Assert.Contains(expected, names);
    }

    [Fact]
    public async Task The_approver_alone_reaches_every_approval_action_through_the_gateway()
    {
        var services = Iam050MovementApprovalPolicyTests.Services();
        var approver = Principal(HrManagementRoles.HrManagementApprover);
        var unreached = new List<string>();
        foreach (var endpoint in ApprovalEndpoints())
        {
            if (!await GatewayPassesAsync(services, endpoint, approver))
                unreached.Add(endpoint.ToString());
        }

        Assert.True(unreached.Count == 0, "The gateway refuses the approver on: " + string.Join("; ", unreached));
    }

    [Fact]
    public async Task A_supervisory_reader_is_refused_on_the_same_post_paths()
    {
        var services = Iam050MovementApprovalPolicyTests.Services();
        var supervisory = Principal(HrManagementRoles.HrCoreSupervisory);
        var admitted = new List<string>();
        foreach (var endpoint in ApprovalEndpoints().Where(e => e.Method != HttpMethods.Get))
        {
            // Where the gateway admits every staff user (D-50-3), the service's explicit policy must refuse.
            if (await GatewayPassesAsync(services, endpoint, supervisory) && await ServicePassesAsync(services, endpoint, supervisory))
                admitted.Add(endpoint.ToString());
        }

        Assert.True(admitted.Count == 0, "hr-core-supervisory passes: " + string.Join("; ", admitted));
    }

    [Fact]
    public void Every_path_the_gateway_opens_to_any_staff_user_is_a_real_action_with_its_own_policy()
    {
        var endpoints = AllEndpoints();
        var opened = new List<(string Module, string Controller, string Action, string Method)>();
        foreach (var (controller, actions) in GatewayApprovalPaths.MovementApprovalActions)
            opened.AddRange(actions.Select(a => ("hr-movements", controller, a.Action, a.Method)));
        foreach (var (controller, actions) in GatewayApprovalPaths.HrCoreWorkflowActions)
            opened.AddRange(actions.Select(a => ("hr-core", controller, a, HttpMethods.Post)));

        foreach (var (module, controller, action, method) in opened)
        {
            var endpoint = endpoints.SingleOrDefault(e =>
                e.Module == module && e.ControllerName == controller && e.Action.Name == action && e.Method == method);
            Assert.True(endpoint is not null, $"{module}/{controller}/{action} ({method}) is opened but is not an action");
            var own = endpoint!.Action.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Policy is not null)
                      || endpoint.Action.GetCustomAttribute<ModulePolicyOverrideAttribute>() is not null;
            Assert.True(own, $"{endpoint} is opened to any staff user but carries no explicit policy of its own");
        }

        // The workflow API's two step actions.
        foreach (var template in GatewayApprovalPaths.HrCoreWorkflowApiTemplates)
        {
            var path = "/api/hr-core/" + template.Replace("{guid}", Id.ToString("D"), StringComparison.Ordinal);
            var endpoint = endpoints.SingleOrDefault(e => e.Method == HttpMethods.Post && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            Assert.True(endpoint is not null, $"{template} is opened but is not an action");
            Assert.Contains(HrManagementPolicies.WorkflowStepAction, endpoint!.Policies);
        }
    }

    [Fact]
    public void Every_action_of_an_opened_controller_names_a_policy()
    {
        var openedControllers = GatewayApprovalPaths.MovementApprovalActions.Keys
            .Concat(GatewayApprovalPaths.HrCoreWorkflowActions.Keys)
            .Append("WorkflowInstances")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unnamed = AllEndpoints()
            .Where(e => openedControllers.Contains(e.ControllerName) && e.Policies.Count == 0 && e.Override is null)
            .Select(e => e.ToString())
            .ToList();
        Assert.True(unnamed.Count == 0, "No explicit policy: " + string.Join("; ", unnamed));
    }

    [Fact]
    public void Every_row_of_the_table_is_a_real_action_guarded_by_the_rows_policy()
    {
        var endpoints = AllEndpoints().Where(e => e.Module == "hr-core").ToList();
        foreach (var row in GatewayHrCoreActionPolicies.MvcRows)
        {
            var endpoint = endpoints.SingleOrDefault(e =>
                e.ControllerName == row.Controller && e.Action.Name == row.Action && e.Method != HttpMethods.Get);
            Assert.True(endpoint is not null, $"row {row.Controller}/{row.Action} is not a mutating action");
            Assert.Contains(row.Policy, endpoint!.Policies);
        }

        foreach (var row in GatewayHrCoreActionPolicies.ApiRows)
        {
            var path = "/api/hr-core/" + row.Template.Replace("{guid}", Id.ToString("D"), StringComparison.Ordinal);
            var endpoint = endpoints.SingleOrDefault(e => e.Method != HttpMethods.Get && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            Assert.True(endpoint is not null, $"row {row.Template} is not a mutating action");
            Assert.Contains(row.Policy, endpoint!.Policies);
        }
    }

    [Fact]
    public void Every_mutating_approval_action_of_hr_core_has_a_row_or_a_named_workflow_path()
    {
        var missing = new List<string>();
        foreach (var endpoint in ApprovalEndpoints().Where(e => e.Module == "hr-core" && e.Method != HttpMethods.Get))
        {
            var inTable = GatewayHrCoreActionPolicies.TryGetPolicy(endpoint.Method, endpoint.Path, out _);
            var named = GatewayApprovalPaths.IsHrCoreWorkflowApprovalPath(endpoint.Method, endpoint.Path);
            if (!inTable && !named)
                missing.Add(endpoint.ToString());
        }

        Assert.True(missing.Count == 0, "Approval actions without a gateway row: " + string.Join("; ", missing));
    }
}
