using HRManagement.SharedKernel.SeparationOfDuties;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Delegation;

/// <summary>
/// PLAN-50 (§5, rule 3): the user holds one of <see cref="Roles"/>, or is the delegate of a delegation in force for
/// <see cref="DelegatedRole"/> (or, with <see cref="AnyDelegableRole"/>, for any delegable role) that covers the action's entity type. The
/// delegated role never reaches the token or <c>IsInRole</c>. With no <see cref="IApprovalDelegationLookup"/> registered (the gateway,
/// HR.Contracts) it is the roles only.
/// </summary>
public sealed class RoleOrDelegationRequirement(IReadOnlyList<string> roles, string? delegatedRole, bool anyDelegableRole = false)
    : IAuthorizationRequirement
{
    public IReadOnlyList<string> Roles { get; } = roles;
    public string? DelegatedRole { get; } = delegatedRole;
    public bool AnyDelegableRole { get; } = anyDelegableRole;
}

/// <summary>
/// The delegation part reads the lookup of the request's scope, and puts the matched delegation in <c>HttpContext.Items</c> under
/// <see cref="ApprovalActingContext.ItemKey"/>. Any failure of the lookup means no delegation (fail-closed); a service account never acts
/// as a delegate. The entity type is the resource's (<see cref="ApprovalEntityTypeResource"/>) or the endpoint's
/// <see cref="ApprovalEntityTypeAttribute"/>; with neither, any entity scope passes this coarse check and the decision point applies it.
/// </summary>
public sealed class RoleOrDelegationHandler(IServiceProvider services) : AuthorizationHandler<RoleOrDelegationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RoleOrDelegationRequirement requirement)
    {
        var user = context.User;
        if (requirement.Roles.Any(user.IsInRole))
        {
            context.Succeed(requirement);
            return;
        }

        if (user.Identity?.IsAuthenticated != true || ServiceCallerPrincipal.IsServiceAccount(user))
            return;

        var http = context.Resource as HttpContext ?? services.GetService<IHttpContextAccessor>()?.HttpContext;
        var lookup = http?.RequestServices?.GetService<IApprovalDelegationLookup>();
        if (http is null || lookup is null)
            return;

        var configuration = services.GetService<IConfiguration>();
        var delegable = DelegationOptions.DelegableRoles(configuration);
        IReadOnlyList<string> candidates = requirement.AnyDelegableRole
            ? delegable
            : requirement.DelegatedRole is { } role && delegable.Contains(role, StringComparer.OrdinalIgnoreCase)
                ? [role]
                : [];
        if (candidates.Count == 0)
            return;

        IReadOnlyList<ActiveDelegation> active;
        try
        {
            active = await lookup.GetActiveForAsync(user, http.RequestAborted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            services.GetService<ILogger<RoleOrDelegationHandler>>()?.LogWarning(ex, "approval delegation lookup failed; no delegation applies");
            return;
        }

        var entityType = EntityTypeOf(context.Resource, http);
        var now = DateTime.UtcNow;
        foreach (var candidate in candidates)
        {
            var match = entityType is null
                ? ApprovalDelegationMatcher.MatchRole(active, candidate, now)
                : ApprovalDelegationMatcher.Match(active, candidate, entityType, now);
            if (match is null)
                continue;

            http.Items[ApprovalActingContext.ItemKey] = match;
            context.Succeed(requirement);
            return;
        }
    }

    public static string? EntityTypeOf(object? resource, HttpContext? http) =>
        resource is ApprovalEntityTypeResource typed
            ? typed.EntityType
            : http?.GetEndpoint()?.Metadata.GetMetadata<ApprovalEntityTypeAttribute>()?.EntityType;
}
