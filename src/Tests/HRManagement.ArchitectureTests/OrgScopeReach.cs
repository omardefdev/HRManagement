using System.Reflection;
using System.Security.Claims;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Delegation;
using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ArchitectureTests;

/// <summary>A restricted user of the reach matrix: the roles of a persona, and whether it has an employee and an active delegation.</summary>
public sealed record RestrictedUser(string Name, string Personas, IReadOnlyList<string> Roles, bool HasEmployee, bool HasDelegation);

/// <summary>Whether each restricted user reaches one action, and through which policies.</summary>
public sealed record ActionReach(
    ActionInfo Action,
    string Verb,
    string? GatewayPath,
    string? GatewayPolicy,
    IReadOnlyList<string> DownstreamPolicies,
    IReadOnlyList<string> ReachedBy);

/// <summary>
/// PLAN-52 (0.3, P-3): who of the restricted users reaches each action of the fifteen web assemblies, decided by the real authorization
/// policies, the way a request meets them: the gateway (<see cref="GatewayPathAuthorizationMiddleware"/>: the segment's module policy, its
/// fine-grained policies and the self-service rule), the module filter with its overrides and its write default, and the action's and the
/// class's policies. A role-or-delegation policy is met by the delegate in the services that register a delegation lookup (HR.Core and
/// HR.Movements, PLAN-50). A controller is <em>actual</em> when some restricted user reaches one of its actions, <em>latent</em> otherwise;
/// an actual controller is never a row of an org-scope list.
/// </summary>
public static class OrgScopeReach
{
    private const string TenantA = "a0000001-0000-4000-8000-000000000001";
    private const string ScopeRif = "a0000001-0000-4000-8000-000000000102";
    private const string SampleId = "a0000001-0000-4000-8000-0000000000f1";

    public static readonly IReadOnlyList<RestrictedUser> Users =
    [
        new("bm-core-logistics", "br1-mgr, br2-mgr", [HrManagementRoles.BranchManager, HrManagementRoles.HrCore, HrManagementRoles.AdminLogistics], false, false),
        new("bm-core", "a-homs-mgr, a-hama-mgr, a-lat-mgr, a-tar-mgr, b-branch-manager, c-branch-manager",
            [HrManagementRoles.BranchManager, HrManagementRoles.HrCore], false, false),
        new("bm-only", "negative-invalid-scope", [HrManagementRoles.BranchManager], false, false),
        new("bm-delegate", "synthetic: br1-mgr's roles, an employee and an active delegation of hr-management-approver",
            [HrManagementRoles.BranchManager, HrManagementRoles.HrCore, HrManagementRoles.AdminLogistics], true, true),
        new("bm-employee", "synthetic: br1-mgr's roles and hr-employee, with an employee",
            [HrManagementRoles.BranchManager, HrManagementRoles.HrCore, HrManagementRoles.AdminLogistics, HrManagementRoles.HrEmployee], true, false),
    ];

    /// <summary>The gateway segment of each service (<c>/ui/{segment}/…</c>); null for the gateway's own pages and the shared controllers.</summary>
    private static readonly IReadOnlyDictionary<string, string?> Segments = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["hr-core"] = "hr-core",
        ["hr-contracts"] = "hr-contracts",
        ["payroll"] = "payroll",
        ["hr-movements"] = "hr-movements",
        ["hr-training"] = "hr-training",
        ["hr-health"] = "hr-health",
        ["compliance"] = "compliance",
        ["idcard"] = "idcard",
        ["admin-logistics"] = "admin-logistics",
        ["it-infra"] = "it-infra",
        ["records-archive"] = "records-archive",
        ["dev-analytics"] = "dev-analytics",
        ["auth-directory"] = "auth-directory",
        ["file-storage"] = "files",
        ["gateway"] = null,
        ["shared"] = null,
    };

    /// <summary>The services whose Program.cs adds the module filter (<c>AddHrModuleAuthorizeFilter</c>): its module and write policy.</summary>
    private static readonly IReadOnlyDictionary<string, (string Module, string Write)> ModuleFilters = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["hr-core"] = (HrManagementPolicies.ModuleHrCore, HrManagementPolicies.HrCoreWrite),
        ["hr-contracts"] = (HrManagementPolicies.ModuleHrContracts, HrManagementPolicies.HrContractsWrite),
        ["payroll"] = (HrManagementPolicies.ModulePayroll, HrManagementPolicies.PayrollWrite),
        ["hr-movements"] = (HrManagementPolicies.ModuleHrMovements, HrManagementPolicies.HrMovementsWrite),
        ["hr-training"] = (HrManagementPolicies.ModuleHrTraining, HrManagementPolicies.HrTrainingWrite),
        ["hr-health"] = (HrManagementPolicies.ModuleHrHealth, HrManagementPolicies.HrHealthWrite),
        ["compliance"] = (HrManagementPolicies.ModuleCompliance, HrManagementPolicies.ComplianceWrite),
        ["idcard"] = (HrManagementPolicies.ModuleIdCard, HrManagementPolicies.IdCardWrite),
        ["admin-logistics"] = (HrManagementPolicies.ModuleAdminLogistics, HrManagementPolicies.AdminLogisticsWrite),
        ["it-infra"] = (HrManagementPolicies.ModuleItInfra, HrManagementPolicies.ItInfraWrite),
        ["records-archive"] = (HrManagementPolicies.ModuleRecordsArchive, HrManagementPolicies.RecordsArchiveWrite),
        ["dev-analytics"] = (HrManagementPolicies.ModuleDevAnalytics, HrManagementPolicies.DevAnalyticsWrite),
        ["auth-directory"] = (HrManagementPolicies.ModuleAuthDirectory, HrManagementPolicies.AuthDirectoryWrite),
    };

    /// <summary>The services that register a delegation lookup (PLAN-50): HR.Core's database, HR.Movements' client.</summary>
    private static readonly IReadOnlySet<string> DelegationServices = new HashSet<string>(["hr-core", "hr-movements"], StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyList<ActionReach>> All = new(() => Compute(ActionCatalog.Actions));

    public static IReadOnlyList<ActionReach> Actions => All.Value;

    /// <summary>The controllers some restricted user reaches, per service, as <c>ControllerName</c>.</summary>
    public static IReadOnlySet<string> ActualControllers(string service) =>
        Actions.Where(a => a.Action.Service == service && a.ReachedBy.Count > 0)
            .Select(a => a.Action.ControllerName)
            .ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<ActionReach> Compute(IEnumerable<ActionInfo> actions)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddHrManagementAuthorizationPolicies();
        using var root = services.BuildServiceProvider();
        var authorization = root.GetRequiredService<IAuthorizationService>();

        var withLookup = new ServiceCollection();
        withLookup.AddSingleton<IConfiguration>(configuration);
        withLookup.AddSingleton<IApprovalDelegationLookup, AnyDelegationLookup>();
        using var lookupProvider = withLookup.BuildServiceProvider();
        using var plainProvider = new ServiceCollection().AddSingleton<IConfiguration>(configuration).BuildServiceProvider();

        var result = new List<ActionReach>();
        foreach (var action in actions)
        {
            var verbs = action.Verbs.Count == 0 ? ["GET"] : action.Verbs;
            foreach (var verb in verbs)
            {
                var path = GatewayPath(action);
                var gatewayPolicy = path is null ? null : GatewayPolicy(verb, path);
                var downstream = DownstreamPolicies(action, verb);
                var reachedBy = new List<string>();
                foreach (var user in Users)
                {
                    var principal = Principal(user);
                    var http = new DefaultHttpContext
                    {
                        RequestServices = user.HasDelegation && DelegationServices.Contains(action.Service) ? lookupProvider : plainProvider,
                        User = principal,
                    };
                    if (Reaches(authorization, principal, http, action, path, gatewayPolicy, downstream))
                        reachedBy.Add(user.Name);
                }

                result.Add(new ActionReach(action, verb, path, gatewayPolicy, downstream.Select(d => d.Display).ToList(), reachedBy));
            }
        }

        return result;
    }

    private static bool Reaches(
        IAuthorizationService authorization,
        ClaimsPrincipal principal,
        HttpContext http,
        ActionInfo action,
        string? path,
        string? gatewayPolicy,
        IReadOnlyList<DownstreamRequirement> downstream)
    {
        if (action.Anonymous)
            return true;
        if (path is not null)
        {
            if (gatewayPolicy is null)
                return false;
            if (!authorization.AuthorizeAsync(principal, resource: null, gatewayPolicy).GetAwaiter().GetResult().Succeeded)
                return false;
        }

        foreach (var requirement in downstream)
        {
            var ok = requirement.Roles is { } roles
                ? roles.Any(principal.IsInRole)
                : authorization.AuthorizeAsync(principal, http, requirement.Policy!).GetAwaiter().GetResult().Succeeded;
            if (!ok)
                return false;
        }

        return true;
    }

    private sealed record DownstreamRequirement(string? Policy, IReadOnlyList<string>? Roles)
    {
        public string Display => Policy ?? "roles:" + string.Join("|", Roles!);
    }

    private static IReadOnlyList<DownstreamRequirement> DownstreamPolicies(ActionInfo action, string verb)
    {
        var list = new List<DownstreamRequirement>();
        var authorize = action.Controller.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>()
            .Concat(action.Method.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>())
            .ToList();
        var servicePolicy = authorize.Any(a => a.Policy?.StartsWith("Service.", StringComparison.Ordinal) == true);
        if (ModuleFilters.TryGetValue(action.Service, out var filter) && !action.Anonymous && !servicePolicy)
        {
            list.Add(new DownstreamRequirement(action.EffectiveOverride ?? filter.Module, null));
            if (ActionInfo.WriteVerbs.Contains(verb) && !authorize.Any(a => !string.IsNullOrWhiteSpace(a.Policy)))
                list.Add(new DownstreamRequirement(filter.Write, null));
        }

        foreach (var a in authorize)
        {
            if (!string.IsNullOrWhiteSpace(a.Policy))
                list.Add(new DownstreamRequirement(a.Policy, null));
            else if (!string.IsNullOrWhiteSpace(a.Roles))
                list.Add(new DownstreamRequirement(null, a.Roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)));
        }

        return list;
    }

    /// <summary>The path a browser uses for the action behind the gateway (<c>/ui/{segment}/{route}</c>); null where the gateway decides
    /// nothing (its own pages, the shared controllers).</summary>
    public static string? GatewayPath(ActionInfo action)
    {
        if (!Segments.TryGetValue(action.Service, out var segment) || segment is null)
            return null;
        var actionName = action.Method.GetCustomAttribute<ActionNameAttribute>()?.Name ?? StripAsync(action.Method.Name);
        var templates = action.RouteTemplates.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        var route = templates.Count > 0
            ? templates[0]
            : $"{action.ControllerName}/{actionName}";
        route = route.Replace("[controller]", action.ControllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", actionName, StringComparison.OrdinalIgnoreCase);
        route = System.Text.RegularExpressions.Regex.Replace(route, "\\{[^}]*\\}", SampleId);
        // The self-service controllers of HR.Core are reached under /ui/ess (the gateway's ui-ess route goes to HR.Core).
        if (action.Service == "hr-core" && GatewayEssOptions.DefaultAllowedControllers[GatewayEssOptions.EssSegment]
                .Contains(action.ControllerName, StringComparer.OrdinalIgnoreCase))
            return $"/ui/{GatewayEssOptions.EssSegment}/{route.TrimStart('/')}";
        return $"/ui/{segment}/{route.TrimStart('/')}";
    }

    private static string StripAsync(string name) =>
        name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5 ? name[..^5] : name;

    private static readonly MethodInfo TryGetPolicyMethod = typeof(GatewayPathAuthorizationMiddleware)
        .GetMethod("TryGetPolicy", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>The policy the gateway applies to the path, as <see cref="GatewayPathAuthorizationMiddleware.InvokeAsync"/> decides it; null
    /// when the gateway answers 404 itself (a self-service path that is not exposed).</summary>
    public static string? GatewayPolicy(string verb, string path)
    {
        object?[] args = [new PathString(path), null, null];
        if (!(bool)TryGetPolicyMethod.Invoke(null, args)!)
            return HrManagementPolicies.GatewayAuthenticated;
        var moduleSegment = (string)args[1]!;
        var modulePolicy = (string)args[2]!;
        return GatewayEssRouting.Decide(verb, path, new GatewayEssOptions()) switch
        {
            GatewayEssDecision.Ess => HrManagementPolicies.ModuleEss,
            GatewayEssDecision.StaticFile => HrManagementPolicies.GatewayAuthenticated,
            GatewayEssDecision.NotExposed => null,
            _ => GatewayPathAuthorizationMiddleware.TryGetFineGrainedPolicy(moduleSegment, verb, new PathString(path), out var fine)
                ? fine
                : modulePolicy,
        };
    }

    public static ClaimsPrincipal Principal(RestrictedUser user)
    {
        var claims = new List<Claim>
        {
            new("sub", "a0000001-0000-4000-8000-0000000000e1"),
            new(ClaimTypes.NameIdentifier, "a0000001-0000-4000-8000-0000000000e1"),
            new(ClaimTypes.Name, user.Name),
            new("tenant_id", TenantA),
            new(HrOrgScopeVisibility.ScopeOrgUnitClaimType, user.Roles.Count == 1 ? "not-a-guid" : ScopeRif),
        };
        if (user.HasEmployee)
            claims.Add(new Claim("employee_id", "a0000001-0000-4000-8000-0000000000e2"));
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "reach-matrix", ClaimTypes.Name, ClaimTypes.Role));
    }

    /// <summary>A delegation of hr-management-approver in force for every entity type.</summary>
    private sealed class AnyDelegationLookup : IApprovalDelegationLookup
    {
        public Task<IReadOnlyList<ActiveDelegation>> GetActiveForAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ActiveDelegation>>(
            [
                new ActiveDelegation(Guid.Parse("a0000001-0000-4000-8000-0000000000d1"), Guid.Parse("a0000001-0000-4000-8000-0000000000d2"),
                    null, "principal", HrManagementRoles.HrManagementApprover, null, DateTime.UtcNow.AddDays(30)),
            ]);
    }
}
