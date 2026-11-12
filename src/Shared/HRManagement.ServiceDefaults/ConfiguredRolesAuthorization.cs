using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// PLAN-51 (D-51-2, D-IAM-4, D-IAM-7): a policy whose roles come from configuration, with today's roles as the default. A service account
/// never passes. When the policy is listed in <c>Authorization:AdminExcludedPolicies</c>, <c>hr-admin</c> is not one of its roles, except
/// on a host in demo mode (<c>Deployment:Mode=demo</c>), where the exclusion is off (an empty array cannot be set reliably through
/// environment variables, so the demo rule lives in code).
/// </summary>
public sealed class ConfiguredRolesRequirement(string policyName, string rolesKey, IReadOnlyList<string> defaultRoles) : IAuthorizationRequirement
{
    public string PolicyName { get; } = policyName;

    public string RolesKey { get; } = rolesKey;

    public IReadOnlyList<string> DefaultRoles { get; } = defaultRoles;
}

public sealed class ConfiguredRolesHandler(IServiceProvider services) : AuthorizationHandler<ConfiguredRolesRequirement>
{
    public const string AdminExcludedPoliciesKey = "Authorization:AdminExcludedPolicies";

    /// <summary>The configuration key of a module policy's roles (D-51-2).</summary>
    public static string PolicyRolesKey(string policyName) => $"Authorization:Policies:{policyName}:Roles";

    /// <summary>The roles the requirement admits under this configuration: configured or default, less hr-admin when excluded.</summary>
    public static IReadOnlyList<string> Roles(IConfiguration? configuration, ConfiguredRolesRequirement requirement)
    {
        var configured = configuration?.GetSection(requirement.RolesKey).Get<string[]>()?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();
        IEnumerable<string> roles = configured is { Count: > 0 } ? configured : requirement.DefaultRoles;
        if (IsAdminExcluded(configuration, requirement.PolicyName))
            roles = roles.Where(r => !string.Equals(r, HrManagementRoles.Admin, StringComparison.OrdinalIgnoreCase));
        return roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>D-IAM-4: the policy is in <c>Authorization:AdminExcludedPolicies</c> and the host is not in demo mode.</summary>
    public static bool IsAdminExcluded(IConfiguration? configuration, string policyName)
    {
        if (configuration is null || ProductionConfigurationGuardOptions.IsDemoMode(configuration))
            return false;
        var excluded = configuration.GetSection(AdminExcludedPoliciesKey).Get<string[]>() ?? [];
        return excluded.Any(p => string.Equals(p?.Trim(), policyName, StringComparison.Ordinal));
    }

    public static bool IsSatisfied(ClaimsPrincipal user, IConfiguration? configuration, ConfiguredRolesRequirement requirement) =>
        user.Identity?.IsAuthenticated == true
        && !ServiceCallerPrincipal.IsServiceAccount(user)
        && Roles(configuration, requirement).Any(user.IsInRole);

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ConfiguredRolesRequirement requirement)
    {
        if (IsSatisfied(context.User, services.GetService<IConfiguration>(), requirement))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class ConfiguredRolesPolicies
{
    /// <summary>D-51-2: <c>{name}</c> admits the roles of <c>Authorization:Policies:{name}:Roles</c>, by default <paramref name="defaults"/>.</summary>
    public static void RolesPolicy(this AuthorizationOptions options, string name, params string[] defaults) =>
        options.AddPolicy(name, p => p.AddRequirements(new ConfiguredRolesRequirement(name, ConfiguredRolesHandler.PolicyRolesKey(name), defaults)));

    /// <summary>A roles policy read from its own configuration key (<c>Gateway:Audit:Roles</c>).</summary>
    public static void RolesPolicyAt(this AuthorizationOptions options, string name, string rolesKey, params string[] defaults) =>
        options.AddPolicy(name, p => p.AddRequirements(new ConfiguredRolesRequirement(name, rolesKey, defaults)));
}
