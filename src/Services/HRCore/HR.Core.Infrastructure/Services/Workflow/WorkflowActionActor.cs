using System.Security.Claims;
using HRManagement.ServiceDefaults;

namespace HR.Core.Infrastructure.Services.Workflow;

/// <summary>
/// PLAN-25 (G-04): who acts on a workflow step — every role of the user (not the first role claim), the employee id
/// (committee membership, appeal reviewer) and the raw branch scope claim (org-scope gates). Build it with
/// <see cref="FromPrincipal"/>.
/// </summary>
public sealed record WorkflowActionActor(
    Guid? UserId,
    string? UserName,
    IReadOnlySet<string> Roles,
    Guid? EmployeeId = null,
    string? ScopeOrgUnitClaim = null,
    bool IsServiceAccount = false)
{
    /// <summary>Compatibility for single-role constructions in tests and services that act as one role.</summary>
    public WorkflowActionActor(Guid? userId, string? userName, string? role)
        : this(userId, userName, ToRoleSet(role is null ? [] : [role]))
    {
    }

    public bool HasRole(string role) => Roles.Contains(role);

    /// <summary>
    /// PLAN-50 (§6): the delegations in force whose delegate is this actor, when the caller loaded them; null means "not loaded", and the
    /// evaluator then asks its lookup. They are never roles of the actor.
    /// </summary>
    public IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation>? Delegations { get; init; }

    /// <summary>
    /// PLAN-50 (§6, P-6): the principal of a delegation, for the second pass of every identity rule. His roles are the delegated role,
    /// and, when he was branch-scoped at creation, the branch role with his stored scope: a delegate never gets a wider scope than his
    /// principal had.
    /// </summary>
    public static WorkflowActionActor PrincipalOf(HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation delegation)
    {
        var roles = new List<string> { delegation.Role };
        if (delegation.PrincipalScopeOrgUnitId is not null)
            roles.Add(HrManagementRoles.BranchManager);
        return new WorkflowActionActor(
            delegation.PrincipalUserId,
            delegation.PrincipalName,
            ToRoleSet(roles),
            delegation.PrincipalEmployeeId,
            delegation.PrincipalScopeOrgUnitId?.ToString("D"));
    }

    /// <summary>The role recorded on the step signature: the step's own role when the actor holds it.</summary>
    public string? SignatureRole(string? stepRoleRequired) =>
        stepRoleRequired is not null && HasRole(stepRoleRequired)
            ? stepRoleRequired
            : Roles.Order(StringComparer.Ordinal).FirstOrDefault();

    /// <summary>All <c>ClaimTypes.Role</c> and <c>role</c> claims (after Keycloak mapping), <c>employee_id</c>, <c>sub</c>.</summary>
    public static WorkflowActionActor FromPrincipal(ClaimsPrincipal user)
    {
        var roles = user.Claims
            .Where(c => c.Type == ClaimTypes.Role
                || c.Type == "role"
                || (c.Subject is { } identity && c.Type == identity.RoleClaimType))
            .Select(c => c.Value);
        var userId = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"), out var id)
            ? id
            : (Guid?)null;
        return new WorkflowActionActor(
            userId,
            user.FindFirstValue("preferred_username") ?? user.Identity?.Name,
            ToRoleSet(roles),
            HrOrgScopeVisibility.GetEmployeeId(user),
            user.FindFirstValue(HrOrgScopeVisibility.ScopeOrgUnitClaimType),
            ServiceCallerPrincipal.IsServiceAccount(user));
    }

    /// <summary>A principal carrying the actor's roles and scope claim, for <see cref="OrgScopeService"/>.</summary>
    public ClaimsPrincipal ToPrincipal()
    {
        var claims = Roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (UserId is Guid userId)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")));
        if (EmployeeId is Guid employeeId)
            claims.Add(new Claim("employee_id", employeeId.ToString("D")));
        if (!string.IsNullOrWhiteSpace(ScopeOrgUnitClaim))
            claims.Add(new Claim(HrOrgScopeVisibility.ScopeOrgUnitClaimType, ScopeOrgUnitClaim));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "workflow-actor", ClaimTypes.Name, ClaimTypes.Role));
    }

    private static IReadOnlySet<string> ToRoleSet(IEnumerable<string> roles) =>
        roles.Where(r => !string.IsNullOrWhiteSpace(r)).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
