using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.ServiceDefaults;

/// <summary>
/// IAM-002 resource-level authorization requirement.
/// Attach to policies that guard org-scoped resources.
/// </summary>
public sealed class OrgScopeRequirement : IAuthorizationRequirement;

/// <summary>
/// IAM-002 fail-closed authorization handler. A branch-scoped user without a valid
/// <c>scope_org_unit_id</c> claim is denied — never granted unrestricted access.
/// Null OrgScope is never treated as all-access.
/// </summary>
public sealed class OrgScopeAuthorizationHandler : AuthorizationHandler<OrgScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrgScopeRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var claim = HrOrgScopeVisibility.GetScopeClaimResult(user);

        switch (claim.Kind)
        {
            case OrgScopeClaimKind.NotScoped:
                context.Succeed(requirement);
                break;

            case OrgScopeClaimKind.Valid:
                context.Succeed(requirement);
                break;

            case OrgScopeClaimKind.InvalidClaim:
                context.Fail(new AuthorizationFailureReason(this, "Missing or invalid scope_org_unit_id claim (IAM-002 fail-closed)."));
                break;
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Registration helper for IAM-002 org-scope authorization handler.
/// </summary>
public static class OrgScopeAuthorizationExtensions
{
    public static IServiceCollection AddOrgScopeAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, OrgScopeAuthorizationHandler>();
        return services;
    }
}
