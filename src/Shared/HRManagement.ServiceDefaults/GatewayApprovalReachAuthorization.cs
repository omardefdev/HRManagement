using System.Security.Claims;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>PLAN-50 (§4): <see cref="HrManagementPolicies.GatewayApprovalReach"/>.</summary>
public sealed class GatewayApprovalReachRequirement : IAuthorizationRequirement;

/// <summary>
/// PLAN-50 (§4, P-4): an interactive staff user. Only the platform's own roles (<see cref="HrManagementRoles.All"/>) count, less the non-staff
/// roles of <c>Authorization:NonStaffRoles</c>; Keycloak's own roles (<c>offline_access</c>, <c>uma_authorization</c>,
/// <c>default-roles-*</c>), which every user carries, never do. A service account, a missing or invalid <c>tenant_id</c> and a user with
/// no staff role are refused. The gateway never looks up a delegation: the service behind the named path decides (D-50-3).
/// </summary>
public sealed class GatewayApprovalReachHandler(IServiceProvider services) : AuthorizationHandler<GatewayApprovalReachRequirement>
{
    public const string NonStaffRolesKey = "Authorization:NonStaffRoles";

    public static readonly IReadOnlyList<string> DefaultNonStaffRoles =
        [HrManagementRoles.HrEmployee, HrManagementRoles.HrExamMarker, "applicant"];

    /// <summary>The configured non-staff roles; the default list when the key is absent or empty.</summary>
    public static IReadOnlyList<string> NonStaffRoles(IConfiguration? configuration)
    {
        var configured = configuration?.GetSection(NonStaffRolesKey).Get<string[]>()?
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToList();
        return configured is { Count: > 0 } ? configured : DefaultNonStaffRoles;
    }

    public static bool IsInteractiveStaff(ClaimsPrincipal user, IReadOnlyCollection<string> nonStaffRoles)
    {
        if (user.Identity?.IsAuthenticated != true || ServiceCallerPrincipal.IsServiceAccount(user))
            return false;

        if (!Guid.TryParse(user.FindFirst(TenantContext.TenantClaimType)?.Value, out var tenantId) || tenantId == Guid.Empty)
            return false;

        return HrManagementRoles.All.Any(role =>
            !nonStaffRoles.Contains(role, StringComparer.OrdinalIgnoreCase) && user.IsInRole(role));
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, GatewayApprovalReachRequirement requirement)
    {
        if (IsInteractiveStaff(context.User, NonStaffRoles(services.GetService<IConfiguration>())))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>PLAN-51 (D-51-3): an interactive staff user (<see cref="GatewayApprovalReachHandler.IsInteractiveStaff"/>, the one definition), or a
/// service account holding <see cref="Scope"/>.</summary>
public sealed class InteractiveStaffOrServiceScopeRequirement(string scope) : IAuthorizationRequirement
{
    public string Scope { get; } = scope;
}

public sealed class InteractiveStaffOrServiceScopeHandler(IServiceProvider services) : AuthorizationHandler<InteractiveStaffOrServiceScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, InteractiveStaffOrServiceScopeRequirement requirement)
    {
        if (ServiceCallerPrincipal.IsServiceAccount(context.User)
                ? ServiceCallerPrincipal.HasScope(context.User, requirement.Scope)
                : GatewayApprovalReachHandler.IsInteractiveStaff(
                    context.User, GatewayApprovalReachHandler.NonStaffRoles(services.GetService<IConfiguration>())))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
